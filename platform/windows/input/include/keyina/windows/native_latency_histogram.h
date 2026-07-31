#pragma once

#include <array>
#include <cstddef>
#include <cstdint>

namespace keyina::windows {

struct NativeLatencySnapshot {
  std::uint64_t sample_count{};
  std::uint64_t p50_ns{};
  std::uint64_t p95_ns{};
  std::uint64_t p99_ns{};
  std::uint64_t maximum_ns{};
  std::uint64_t mean_ns{};
};

class NativeLatencyHistogram {
 public:
  static constexpr std::size_t kBucketCount = 64;

  void RecordNanoseconds(std::uint64_t nanoseconds) noexcept;
  [[nodiscard]] NativeLatencySnapshot Snapshot() const noexcept;
  void Clear() noexcept;

 private:
  [[nodiscard]] static std::size_t BucketIndex(
      std::uint64_t nanoseconds) noexcept;
  [[nodiscard]] static std::uint64_t BucketUpperBound(
      std::size_t index) noexcept;
  [[nodiscard]] std::uint64_t PercentileUpperBound(
      std::uint32_t percentile) const noexcept;

  std::array<std::uint64_t, kBucketCount> buckets_{};
  std::uint64_t sample_count_{};
  std::uint64_t sum_nanoseconds_{};
  std::uint64_t maximum_nanoseconds_{};
};

}  // namespace keyina::windows
