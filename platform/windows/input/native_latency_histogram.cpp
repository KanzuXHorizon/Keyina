#include <keyina/windows/native_latency_histogram.h>

#include <algorithm>
#include <bit>
#include <limits>

namespace keyina::windows {
namespace {

constexpr std::uint64_t kMaximumValue =
    std::numeric_limits<std::uint64_t>::max();

std::uint64_t SaturatingAdd(
    std::uint64_t left,
    std::uint64_t right) noexcept {
  return kMaximumValue - left < right ? kMaximumValue : left + right;
}

std::uint64_t PercentileRank(
    std::uint64_t sample_count,
    std::uint32_t percentile) noexcept {
  const std::uint64_t quotient = sample_count / 100;
  const std::uint64_t remainder = sample_count % 100;
  return quotient * percentile +
      ((remainder * percentile) + 99) / 100;
}

}  // namespace

void NativeLatencyHistogram::RecordNanoseconds(
    std::uint64_t nanoseconds) noexcept {
  const std::size_t index = BucketIndex(nanoseconds);
  if (buckets_[index] != kMaximumValue) {
    ++buckets_[index];
  }
  if (sample_count_ != kMaximumValue) {
    ++sample_count_;
  }
  sum_nanoseconds_ = SaturatingAdd(sum_nanoseconds_, nanoseconds);
  maximum_nanoseconds_ = std::max(maximum_nanoseconds_, nanoseconds);
}

NativeLatencySnapshot NativeLatencyHistogram::Snapshot() const noexcept {
  if (sample_count_ == 0) {
    return {};
  }
  return NativeLatencySnapshot{
      sample_count_,
      PercentileUpperBound(50),
      PercentileUpperBound(95),
      PercentileUpperBound(99),
      maximum_nanoseconds_,
      sum_nanoseconds_ / sample_count_,
  };
}

void NativeLatencyHistogram::Clear() noexcept {
  buckets_.fill(0);
  sample_count_ = 0;
  sum_nanoseconds_ = 0;
  maximum_nanoseconds_ = 0;
}

std::size_t NativeLatencyHistogram::BucketIndex(
    std::uint64_t nanoseconds) noexcept {
  if (nanoseconds <= 1) {
    return 0;
  }
  const auto width = static_cast<std::size_t>(
      std::bit_width(nanoseconds - 1));
  return std::min(width, kBucketCount - 1);
}

std::uint64_t NativeLatencyHistogram::BucketUpperBound(
    std::size_t index) noexcept {
  if (index >= kBucketCount - 1) {
    return kMaximumValue;
  }
  return std::uint64_t{1} << index;
}

std::uint64_t NativeLatencyHistogram::PercentileUpperBound(
    std::uint32_t percentile) const noexcept {
  const std::uint64_t target = PercentileRank(sample_count_, percentile);
  std::uint64_t cumulative = 0;
  for (std::size_t index = 0; index < buckets_.size(); ++index) {
    cumulative = SaturatingAdd(cumulative, buckets_[index]);
    if (cumulative >= target) {
      return BucketUpperBound(index);
    }
  }
  return kMaximumValue;
}

}  // namespace keyina::windows
