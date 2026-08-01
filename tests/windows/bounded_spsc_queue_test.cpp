#include <keyina/windows/bounded_spsc_queue.h>

#include "../test_support.h"

#include <cstddef>

namespace {

struct WorkItem {
  int value{};
};

}  // namespace

KEYINA_TEST(bounded_spsc_queue_preserves_fifo_order) {
  keyina::windows::BoundedSpscQueue<WorkItem, 4> queue;

  for (int value = 1; value <= 3; ++value) {
    auto* item = queue.TryReserveProducer();
    KEYINA_EXPECT_TRUE(item != nullptr);
    item->value = value;
    KEYINA_EXPECT_TRUE(queue.CommitProducer());
  }

  KEYINA_EXPECT_TRUE(queue.TryReserveProducer() == nullptr);
  for (int expected = 1; expected <= 3; ++expected) {
    auto* item = queue.TryPeekConsumer();
    KEYINA_EXPECT_TRUE(item != nullptr);
    KEYINA_EXPECT_EQ(item->value, expected);
    KEYINA_EXPECT_TRUE(queue.PopConsumer());
  }
  KEYINA_EXPECT_TRUE(queue.empty());
}

KEYINA_TEST(bounded_spsc_queue_cancel_keeps_capacity_available) {
  keyina::windows::BoundedSpscQueue<WorkItem, 3> queue;

  auto* cancelled = queue.TryReserveProducer();
  KEYINA_EXPECT_TRUE(cancelled != nullptr);
  cancelled->value = 99;
  queue.CancelProducer();
  KEYINA_EXPECT_TRUE(queue.empty());

  for (int value = 1; value <= 2; ++value) {
    auto* item = queue.TryReserveProducer();
    KEYINA_EXPECT_TRUE(item != nullptr);
    item->value = value;
    KEYINA_EXPECT_TRUE(queue.CommitProducer());
  }
  KEYINA_EXPECT_TRUE(queue.TryReserveProducer() == nullptr);
}

KEYINA_TEST(bounded_spsc_queue_reuses_slots_after_consumer_progress) {
  keyina::windows::BoundedSpscQueue<WorkItem, 3> queue;

  for (int value = 1; value <= 2; ++value) {
    auto* item = queue.TryReserveProducer();
    KEYINA_EXPECT_TRUE(item != nullptr);
    item->value = value;
    KEYINA_EXPECT_TRUE(queue.CommitProducer());
  }

  KEYINA_EXPECT_EQ(queue.TryPeekConsumer()->value, 1);
  KEYINA_EXPECT_TRUE(queue.PopConsumer());

  auto* wrapped = queue.TryReserveProducer();
  KEYINA_EXPECT_TRUE(wrapped != nullptr);
  wrapped->value = 3;
  KEYINA_EXPECT_TRUE(queue.CommitProducer());

  KEYINA_EXPECT_EQ(queue.TryPeekConsumer()->value, 2);
  KEYINA_EXPECT_TRUE(queue.PopConsumer());
  KEYINA_EXPECT_EQ(queue.TryPeekConsumer()->value, 3);
  KEYINA_EXPECT_TRUE(queue.PopConsumer());
  KEYINA_EXPECT_TRUE(queue.empty());
}
