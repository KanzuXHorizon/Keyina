#include <algorithm>
#include <array>
#include <atomic>
#include <chrono>
#include <cstddef>
#include <cstdint>
#include <cstdlib>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <new>
#include <optional>
#include <sstream>
#include <string>
#include <string_view>
#include <vector>

#include <keyina/context_guard.h>
#include <keyina/engine.h>
#include <keyina/vietnamese_syllable.h>

namespace {
std::atomic<std::uint64_t> g_allocation_count{0};
}

void* operator new(std::size_t size) {
  g_allocation_count.fetch_add(1, std::memory_order_relaxed);
  if (void* memory = std::malloc(size)) {
    return memory;
  }
  throw std::bad_alloc{};
}

void* operator new[](std::size_t size) {
  g_allocation_count.fetch_add(1, std::memory_order_relaxed);
  if (void* memory = std::malloc(size)) {
    return memory;
  }
  throw std::bad_alloc{};
}

void operator delete(void* memory) noexcept { std::free(memory); }
void operator delete[](void* memory) noexcept { std::free(memory); }
void operator delete(void* memory, std::size_t) noexcept { std::free(memory); }
void operator delete[](void* memory, std::size_t) noexcept { std::free(memory); }

namespace {

using Clock = std::chrono::steady_clock;

struct Result {
  std::string_view name;
  double median_ns;
  double p95_ns;
  double p99_ns;
  double maximum_ns;
  double allocations_per_operation;
  double allocation_budget;
  bool budget_pass;
};

struct Options {
  std::size_t warmup = 20'000;
  std::size_t iterations = 100'000;
  std::optional<std::filesystem::path> output_json;
  std::optional<std::filesystem::path> output_csv;
};

std::optional<std::size_t> ParseCount(std::string_view value) {
  try {
    const auto parsed = std::stoull(std::string{value});
    if (parsed == 0) {
      return std::nullopt;
    }
    return static_cast<std::size_t>(parsed);
  } catch (...) {
    return std::nullopt;
  }
}

std::optional<Options> ParseOptions(int argc, char** argv) {
  Options options;
  for (int index = 1; index < argc; ++index) {
    const std::string_view argument{argv[index]};
    const auto next_value = [&]() -> std::optional<std::string_view> {
      if (index + 1 >= argc) {
        return std::nullopt;
      }
      return std::string_view{argv[++index]};
    };

    if (argument == "--warmup") {
      const auto value = next_value();
      const auto parsed = value ? ParseCount(*value) : std::nullopt;
      if (!parsed) {
        return std::nullopt;
      }
      options.warmup = *parsed;
    } else if (argument == "--iterations") {
      const auto value = next_value();
      const auto parsed = value ? ParseCount(*value) : std::nullopt;
      if (!parsed) {
        return std::nullopt;
      }
      options.iterations = *parsed;
    } else if (argument == "--output-json") {
      const auto value = next_value();
      if (!value || value->empty()) {
        return std::nullopt;
      }
      options.output_json = std::filesystem::path{*value};
    } else if (argument == "--output-csv") {
      const auto value = next_value();
      if (!value || value->empty()) {
        return std::nullopt;
      }
      options.output_csv = std::filesystem::path{*value};
    } else {
      return std::nullopt;
    }
  }
  return options;
}

std::string JsonEscape(std::string_view value) {
  std::string escaped;
  escaped.reserve(value.size());
  for (const char character : value) {
    switch (character) {
      case '\\':
        escaped += "\\\\";
        break;
      case '"':
        escaped += "\\\"";
        break;
      case '\n':
        escaped += "\\n";
        break;
      case '\r':
        escaped += "\\r";
        break;
      case '\t':
        escaped += "\\t";
        break;
      default:
        escaped.push_back(character);
        break;
    }
  }
  return escaped;
}

double Percentile(const std::vector<std::uint64_t>& sorted,
                  std::size_t numerator, std::size_t denominator) {
  const std::size_t index =
      ((sorted.size() - 1) * numerator + denominator - 1) / denominator;
  return static_cast<double>(sorted[index]);
}

template <typename Operation>
Result Measure(std::string_view name, Operation&& operation, std::size_t warmup,
               std::size_t iterations, std::uint64_t& checksum,
               double allocation_budget = 0.0) {
  for (std::size_t index = 0; index < warmup; ++index) {
    checksum += operation();
  }

  const auto allocation_start =
      g_allocation_count.load(std::memory_order_relaxed);
  for (std::size_t index = 0; index < iterations; ++index) {
    checksum += operation();
  }
  const auto allocation_finish =
      g_allocation_count.load(std::memory_order_relaxed);
  const double allocations_per_operation =
      static_cast<double>(allocation_finish - allocation_start) /
      static_cast<double>(iterations);

  std::vector<std::uint64_t> samples;
  samples.reserve(iterations);
  for (std::size_t index = 0; index < iterations; ++index) {
    const auto start = Clock::now();
    checksum += operation();
    const auto finish = Clock::now();
    samples.push_back(static_cast<std::uint64_t>(
        std::chrono::duration_cast<std::chrono::nanoseconds>(finish - start)
            .count()));
  }
  std::sort(samples.begin(), samples.end());

  return Result{
      name,
      Percentile(samples, 1, 2),
      Percentile(samples, 95, 100),
      Percentile(samples, 99, 100),
      static_cast<double>(samples.back()),
      allocations_per_operation,
      allocation_budget,
      allocations_per_operation <= allocation_budget,
  };
}

std::string CompilerName() {
#if defined(_MSC_VER)
  return "msvc-" + std::to_string(_MSC_VER);
#elif defined(__clang__)
  return "clang-" + std::to_string(__clang_major__) + "." +
         std::to_string(__clang_minor__);
#elif defined(__GNUC__)
  return "gcc-" + std::to_string(__GNUC__) + "." +
         std::to_string(__GNUC_MINOR__);
#else
  return "unknown";
#endif
}

std::string OperatingSystem() {
#if defined(_WIN32)
  return "windows";
#elif defined(__linux__)
  return "linux";
#else
  return "unknown";
#endif
}

std::string BuildType() {
#ifdef NDEBUG
  return "release";
#else
  return "debug";
#endif
}

std::string Processor() {
#if defined(_MSC_VER)
  char* identifier = nullptr;
  std::size_t size = 0;
  if (_dupenv_s(&identifier, &size, "PROCESSOR_IDENTIFIER") != 0 ||
      identifier == nullptr) {
    return "unknown";
  }
  const std::string result{identifier};
  std::free(identifier);
  return result;
#else
  const char* identifier = std::getenv("PROCESSOR_IDENTIFIER");
  return identifier == nullptr ? "unknown" : identifier;
#endif
}

std::size_t TypeSequence(keyina::Engine& engine, std::u32string_view sequence) {
  std::size_t checksum = 0;
  for (const char32_t character : sequence) {
    const auto edit = engine.ProcessView(
        {keyina::KeyKind::Character, character, false, false, false});
    checksum += edit.insert.size() + edit.erase_codepoints + edit.consumed;
  }
  return checksum;
}

void TypePrefix(keyina::Engine& engine, std::u32string_view prefix) {
  static_cast<void>(TypeSequence(engine, prefix));
}

}  // namespace

int main(int argc, char** argv) {
  const auto options = ParseOptions(argc, argv);
  if (!options) {
    std::cerr << "Usage: keyina_bench [--warmup N] [--iterations N] "
                 "[--output-json PATH] [--output-csv PATH]\n";
    return 2;
  }

  const std::size_t kWarmup = options->warmup;
  const std::size_t kIterations = options->iterations;
  std::uint64_t checksum = 0;
  std::vector<Result> results;
  results.reserve(12);

  keyina::Engine ascii_engine;
  results.push_back(Measure(
      "ascii_pass_through",
      [&]() {
        ascii_engine.Reset();
        const auto edit = ascii_engine.Process(
            {keyina::KeyKind::Character, U'b', false, false, false});
        return edit.insert.size() + edit.erase_codepoints + edit.consumed;
      },
      kWarmup, kIterations, checksum));

  keyina::Engine modifier_engine;
  results.push_back(Measure(
      "letter_modifier",
      [&]() {
        modifier_engine.Reset();
        TypePrefix(modifier_engine, U"a");
        const auto edit = modifier_engine.Process(
            {keyina::KeyKind::Character, U'a', false, false, false});
        return edit.insert.size() + edit.erase_codepoints + edit.consumed;
      },
      kWarmup, kIterations, checksum));

  keyina::Engine tone_engine;
  results.push_back(Measure(
      "tone_update",
      [&]() {
        tone_engine.Reset();
        TypePrefix(tone_engine, U"a");
        const auto edit = tone_engine.Process(
            {keyina::KeyKind::Character, U's', false, false, false});
        return edit.insert.size() + edit.erase_codepoints + edit.consumed;
      },
      kWarmup, kIterations, checksum));

  keyina::Engine complete_word_engine;
  results.push_back(Measure(
      "complete_word_tieengs",
      [&]() {
        complete_word_engine.Reset();
        return TypeSequence(complete_word_engine, U"tieengs");
      },
      kWarmup, kIterations, checksum));

  results.push_back(Measure(
      "complete_word_Vieetj",
      [&]() {
        complete_word_engine.Reset();
        return TypeSequence(complete_word_engine, U"Vieetj");
      },
      kWarmup, kIterations, checksum));

  results.push_back(Measure(
      "delayed_modifier_truowcs",
      [&]() {
        complete_word_engine.Reset();
        return TypeSequence(complete_word_engine, U"truowcs");
      },
      kWarmup, kIterations, checksum));

  keyina::Engine backspace_engine;
  results.push_back(Measure(
      "backspace_recomposition",
      [&]() {
        backspace_engine.Reset();
        auto value = TypeSequence(backspace_engine, U"tieengs");
        const auto edit = backspace_engine.Process(
            {keyina::KeyKind::Backspace, U'\0', false, false, false});
        return value + edit.insert.size() + edit.erase_codepoints +
               edit.consumed;
      },
      kWarmup, kIterations, checksum));

  keyina::Engine url_engine;
  results.push_back(Measure(
      "guard_protected_url",
      [&]() {
        url_engine.Reset();
        TypePrefix(url_engine, U"https://example.co");
        const auto edit = url_engine.Process(
            {keyina::KeyKind::Character, U'm', false, false, false});
        return edit.insert.size() + edit.erase_codepoints + edit.consumed;
      },
      kWarmup, kIterations, checksum));

  keyina::Engine email_engine;
  results.push_back(Measure(
      "guard_protected_email",
      [&]() {
        email_engine.Reset();
        TypePrefix(email_engine, U"user@example.co");
        const auto edit = email_engine.Process(
            {keyina::KeyKind::Character, U'm', false, false, false});
        return edit.insert.size() + edit.erase_codepoints + edit.consumed;
      },
      kWarmup, kIterations, checksum, 1.0));

  results.push_back(Measure(
      "valid_syllable_analysis",
      [&]() {
        const auto analysis = keyina::AnalyzeVietnameseSyllable(U"nghiêng");
        return static_cast<std::size_t>(analysis.status) +
               static_cast<std::size_t>(analysis.error) + analysis.nucleus.size();
      },
      kWarmup, kIterations, checksum));

  keyina::Engine restore_engine({
      .tone_placement = keyina::TonePlacement::Modern,
      .application_bypass = false,
      .restore_invalid_word = true,
  });
  results.push_back(Measure(
      "invalid_boundary_restore",
      [&]() {
        restore_engine.Reset();
        TypePrefix(restore_engine, U"haahhaahhaahh");
        const auto edit = restore_engine.Process(
            {keyina::KeyKind::CommitBoundary, U' ', false, false, false});
        return edit.insert.size() + edit.erase_codepoints + edit.consumed;
      },
      kWarmup, kIterations, checksum, 1.0));

  const std::u32string token(64, U'a');
  results.push_back(Measure(
      "context_guard_64_codepoints",
      [&]() {
        const auto result = keyina::ClassifyToken(token, {});
        return static_cast<std::size_t>(result.transform) +
               static_cast<std::size_t>(result.reason);
      },
      kWarmup, kIterations, checksum));

  std::ostringstream json;
  json << "{\n"
       << "  \"schema_version\": 3,\n"
       << "  \"environment\": {\n"
       << "    \"os\": \"" << JsonEscape(OperatingSystem()) << "\",\n"
       << "    \"processor\": \"" << JsonEscape(Processor()) << "\",\n"
       << "    \"compiler\": \"" << JsonEscape(CompilerName()) << "\",\n"
       << "    \"build_type\": \"" << JsonEscape(BuildType()) << "\",\n"
       << "    \"warmup_iterations\": " << kWarmup << ",\n"
       << "    \"measured_iterations\": " << kIterations << "\n"
       << "  },\n"
       << "  \"cases\": [\n";

  for (std::size_t index = 0; index < results.size(); ++index) {
    const auto& result = results[index];
    json << "    {\"name\": \"" << JsonEscape(result.name)
         << "\", \"median_ns\": " << result.median_ns
         << ", \"p95_ns\": " << result.p95_ns
         << ", \"p99_ns\": " << result.p99_ns
         << ", \"max_ns\": " << result.maximum_ns
         << ", \"allocations_per_operation\": "
         << result.allocations_per_operation
         << ", \"allocation_budget\": " << result.allocation_budget
         << ", \"budget_pass\": "
         << (result.budget_pass ? "true" : "false") << "}";
    json << (index + 1 == results.size() ? "\n" : ",\n");
  }
  json << "  ],\n"
       << "  \"checksum\": " << checksum << "\n"
       << "}\n";

  std::ostringstream csv;
  csv << "name,median_ns,p95_ns,p99_ns,max_ns,allocations_per_operation,"
         "allocation_budget,budget_pass\n";
  for (const auto& result : results) {
    csv << result.name << ',' << result.median_ns << ',' << result.p95_ns << ','
        << result.p99_ns << ',' << result.maximum_ns << ','
        << result.allocations_per_operation << ',' << result.allocation_budget
        << ',' << (result.budget_pass ? "true" : "false") << '\n';
  }

  std::cout << json.str();
  const auto write_file = [](const std::filesystem::path& path,
                             const std::string& contents) {
    if (path.has_parent_path()) {
      std::filesystem::create_directories(path.parent_path());
    }
    std::ofstream output{path, std::ios::binary | std::ios::trunc};
    output.exceptions(std::ios::failbit | std::ios::badbit);
    output << contents;
  };

  try {
    if (options->output_json) {
      write_file(*options->output_json, json.str());
    }
    if (options->output_csv) {
      write_file(*options->output_csv, csv.str());
    }
  } catch (const std::exception& error) {
    std::cerr << "Failed to write benchmark report: " << error.what() << '\n';
    return 3;
  }
  return std::all_of(results.begin(), results.end(),
                     [](const Result& result) { return result.budget_pass; })
             ? 0
             : 1;
}
