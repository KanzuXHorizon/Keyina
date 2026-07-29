#pragma once

#include <windows.h>

#include <atomic>
#include <cstdint>
#include <functional>
#include <mutex>
#include <string>
#include <thread>

#include <keyina/ipc_protocol.h>

namespace keyina::tsf {

class PipeClient final {
 public:
  using EnvelopeHandler = std::function<void(ipc::Envelope)>;

  PipeClient(std::wstring pipe_name,
             ipc::SessionId session_id,
             EnvelopeHandler handler);
  ~PipeClient();

  PipeClient(const PipeClient&) = delete;
  PipeClient& operator=(const PipeClient&) = delete;

  [[nodiscard]] bool Start() noexcept;
  void SetFocused(bool focused, std::uint64_t focus_generation) noexcept;
  void Stop() noexcept;

 private:
  enum class TransferResult {
    Success,
    Disconnected,
    StateChanged,
    Stopped,
  };

  struct FocusSnapshot {
    bool focused{false};
    std::uint64_t focus_generation{};
    std::uint64_t version{};
  };

  void Run() noexcept;
  [[nodiscard]] FocusSnapshot Snapshot() const noexcept;
  [[nodiscard]] bool WaitForFocused(FocusSnapshot& snapshot) noexcept;
  [[nodiscard]] HANDLE Connect(const FocusSnapshot& snapshot) noexcept;
  [[nodiscard]] bool SendHello(HANDLE pipe,
                               const FocusSnapshot& snapshot) noexcept;
  [[nodiscard]] TransferResult ReceiveEnvelope(
      HANDLE pipe,
      const FocusSnapshot& snapshot,
      ipc::Envelope& envelope) noexcept;
  [[nodiscard]] TransferResult TransferExact(
      HANDLE pipe,
      bool write,
      std::span<std::uint8_t> buffer) noexcept;
  [[nodiscard]] bool IsCurrent(const FocusSnapshot& snapshot) const noexcept;

  std::wstring full_pipe_name_;
  ipc::SessionId session_id_;
  EnvelopeHandler handler_;
  HANDLE stop_event_{nullptr};
  HANDLE state_event_{nullptr};
  mutable std::mutex state_mutex_;
  bool focused_{false};
  std::uint64_t focus_generation_{};
  std::uint64_t state_version_{};
  std::thread worker_;
  std::atomic<bool> started_{false};
};

}  // namespace keyina::tsf
