#include <keyina/windows/native_latency_histogram.h>

#include "../test_support.h"

#include <array>
#include <cstdint>
#include <limits>

KEYINA_TEST(native_latency_histogram_empty_snapshot_is_zero) {
  keyina::windows::NativeLatencyHistogram histogram;

  const auto snapshot = histogram.Snapshot();

  KEYINA_EXPECT_EQ(snapshot.sample_count, std::uint64_t{0});
  KEYINA_EXPECT_EQ(snapshot.p50_ns, std::uint64_t{0});
  KEYINA_EXPECT_EQ(snapshot.p95_ns, std::uint64_t{0});
  KEYINA_EXPECT_EQ(snapshot.p99_ns, std::uint64_t{0});
  KEYINA_EXPECT_EQ(snapshot.maximum_ns, std::uint64_t{0});
  KEYINA_EXPECT_EQ(snapshot.mean_ns, std::uint64_t{0});
}

KEYINA_TEST(native_latency_histogram_reports_bounded_percentiles_and_exact_totals) {
  keyina::windows::NativeLatencyHistogram histogram;
  constexpr std::array<std::uint64_t, 9> samples{
      1, 2, 3, 4, 5, 8, 9, 16, 1000,
  };
  for (const auto sample : samples) {
    histogram.RecordNanoseconds(sample);
  }

  const auto snapshot = histogram.Snapshot();

  KEYINA_EXPECT_EQ(snapshot.sample_count, std::uint64_t{9});
  KEYINA_EXPECT_EQ(snapshot.p50_ns, std::uint64_t{8});
  KEYINA_EXPECT_EQ(snapshot.p95_ns, std::uint64_t{1024});
  KEYINA_EXPECT_EQ(snapshot.p99_ns, std::uint64_t{1024});
  KEYINA_EXPECT_EQ(snapshot.maximum_ns, std::uint64_t{1000});
  KEYINA_EXPECT_EQ(snapshot.mean_ns, std::uint64_t{116});
}

KEYINA_TEST(native_latency_histogram_clear_resets_all_state) {
  keyina::windows::NativeLatencyHistogram histogram;
  histogram.RecordNanoseconds(500);
  histogram.RecordNanoseconds(1000);

  histogram.Clear();
  const auto snapshot = histogram.Snapshot();

  KEYINA_EXPECT_EQ(snapshot.sample_count, std::uint64_t{0});
  KEYINA_EXPECT_EQ(snapshot.maximum_ns, std::uint64_t{0});
  KEYINA_EXPECT_EQ(snapshot.mean_ns, std::uint64_t{0});
}

KEYINA_TEST(native_latency_histogram_saturates_the_final_bucket) {
  keyina::windows::NativeLatencyHistogram histogram;
  histogram.RecordNanoseconds(std::numeric_limits<std::uint64_t>::max());

  const auto snapshot = histogram.Snapshot();

  KEYINA_EXPECT_EQ(snapshot.sample_count, std::uint64_t{1});
  KEYINA_EXPECT_EQ(
      snapshot.p50_ns, std::numeric_limits<std::uint64_t>::max());
  KEYINA_EXPECT_EQ(
      snapshot.p95_ns, std::numeric_limits<std::uint64_t>::max());
  KEYINA_EXPECT_EQ(
      snapshot.p99_ns, std::numeric_limits<std::uint64_t>::max());
  KEYINA_EXPECT_EQ(
      snapshot.maximum_ns, std::numeric_limits<std::uint64_t>::max());
  KEYINA_EXPECT_EQ(
      snapshot.mean_ns, std::numeric_limits<std::uint64_t>::max());
}
