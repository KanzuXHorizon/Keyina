#pragma once

#include <array>
#include <atomic>
#include <cstddef>

namespace keyina::windows {

template <typename T, std::size_t Storage>
class BoundedSpscQueue {
  static_assert(Storage >= 2, "SPSC queue requires one sentinel slot.");

 public:
  static constexpr std::size_t capacity() noexcept { return Storage - 1; }

  [[nodiscard]] T* TryReserveProducer() noexcept {
    if (producer_reserved_) {
      return nullptr;
    }
    const std::size_t tail = tail_.load(std::memory_order_relaxed);
    const std::size_t next = (tail + 1) % Storage;
    if (next == head_.load(std::memory_order_acquire)) {
      return nullptr;
    }
    producer_reserved_ = true;
    reserved_next_tail_ = next;
    return &items_[tail];
  }

  [[nodiscard]] bool CommitProducer() noexcept {
    if (!producer_reserved_) {
      return false;
    }
    tail_.store(reserved_next_tail_, std::memory_order_release);
    producer_reserved_ = false;
    reserved_next_tail_ = 0;
    return true;
  }

  void CancelProducer() noexcept {
    producer_reserved_ = false;
    reserved_next_tail_ = 0;
  }

  [[nodiscard]] T* TryPeekConsumer() noexcept {
    const std::size_t head = head_.load(std::memory_order_relaxed);
    if (head == tail_.load(std::memory_order_acquire)) {
      return nullptr;
    }
    return &items_[head];
  }

  [[nodiscard]] bool PopConsumer() noexcept {
    const std::size_t head = head_.load(std::memory_order_relaxed);
    if (head == tail_.load(std::memory_order_acquire)) {
      return false;
    }
    head_.store((head + 1) % Storage, std::memory_order_release);
    return true;
  }

  [[nodiscard]] bool empty() const noexcept {
    return head_.load(std::memory_order_acquire) ==
        tail_.load(std::memory_order_acquire);
  }

  void ResetAfterShutdown() noexcept {
    producer_reserved_ = false;
    reserved_next_tail_ = 0;
    head_.store(0, std::memory_order_relaxed);
    tail_.store(0, std::memory_order_relaxed);
  }

 private:
  std::array<T, Storage> items_{};
  std::atomic_size_t head_{};
  std::atomic_size_t tail_{};
  std::size_t reserved_next_tail_{};
  bool producer_reserved_{false};
};

}  // namespace keyina::windows
