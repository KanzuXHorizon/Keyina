#include <keyina/tsf/pipe_client.h>

#include <array>
#include <chrono>
#include <limits>
#include <span>
#include <string>
#include <utility>
#include <vector>

namespace keyina::tsf {
namespace {

constexpr DWORD kRetryMilliseconds = 100;
constexpr DWORD kPipeTimeoutMilliseconds = 1'000;

std::uint32_t ReadLittleEndian32(
    std::span<const std::uint8_t, ipc::kHeaderSize> header,
    std::size_t offset) noexcept {
  return static_cast<std::uint32_t>(header[offset]) |
         (static_cast<std::uint32_t>(header[offset + 1]) << 8U) |
         (static_cast<std::uint32_t>(header[offset + 2]) << 16U) |
         (static_cast<std::uint32_t>(header[offset + 3]) << 24U);
}

std::string HelloPayload() {
  const DWORD process_id = GetCurrentProcessId();
  const DWORD thread_id = GetCurrentThreadId();
  return "pid=" + std::to_string(process_id) +
         ";tid=" + std::to_string(thread_id) +
         ";cap=external_text";
}

}  // namespace

PipeClient::PipeClient(std::wstring pipe_name,
                       ipc::SessionId session_id,
                       EnvelopeHandler handler)
    : full_pipe_name_(L"\\\\.\\pipe\\" + std::move(pipe_name)),
      session_id_(session_id),
      handler_(std::move(handler)),
      stop_event_(CreateEventW(nullptr, TRUE, FALSE, nullptr)),
      state_event_(CreateEventW(nullptr, TRUE, FALSE, nullptr)) {}

PipeClient::~PipeClient() {
  Stop();
  if (state_event_ != nullptr) {
    CloseHandle(state_event_);
  }
  if (stop_event_ != nullptr) {
    CloseHandle(stop_event_);
  }
}

bool PipeClient::Start() noexcept {
  if (stop_event_ == nullptr || state_event_ == nullptr ||
      full_pipe_name_.size() <= 9 || !handler_ ||
      started_.exchange(true, std::memory_order_acq_rel)) {
    return false;
  }

  try {
    worker_ = std::thread([this] { Run(); });
    return true;
  } catch (...) {
    started_.store(false, std::memory_order_release);
    return false;
  }
}

void PipeClient::SetFocused(bool focused,
                            std::uint64_t focus_generation) noexcept {
  {
    std::lock_guard lock(state_mutex_);
    focused_ = focused;
    focus_generation_ = focus_generation;
    ++state_version_;
  }
  if (state_event_ != nullptr) {
    SetEvent(state_event_);
  }
}

void PipeClient::Stop() noexcept {
  if (!started_.exchange(false, std::memory_order_acq_rel)) {
    return;
  }
  if (stop_event_ != nullptr) {
    SetEvent(stop_event_);
  }
  if (state_event_ != nullptr) {
    SetEvent(state_event_);
  }
  if (worker_.joinable()) {
    worker_.join();
  }
}

void PipeClient::Run() noexcept {
  try {
    while (WaitForSingleObject(stop_event_, 0) != WAIT_OBJECT_0) {
      FocusSnapshot snapshot{};
      if (!WaitForFocused(snapshot)) {
        return;
      }

      HANDLE pipe = Connect(snapshot);
      if (pipe == INVALID_HANDLE_VALUE) {
        continue;
      }

      if (!SendHello(pipe, snapshot)) {
        CloseHandle(pipe);
        continue;
      }

      while (IsCurrent(snapshot) &&
             WaitForSingleObject(stop_event_, 0) != WAIT_OBJECT_0) {
        ipc::Envelope envelope;
        const TransferResult received =
            ReceiveEnvelope(pipe, snapshot, envelope);
        if (received != TransferResult::Success) {
          break;
        }
        if (envelope.session_id != session_id_ ||
            envelope.focus_generation != snapshot.focus_generation) {
          continue;
        }
        if (envelope.message_type != ipc::MessageType::FinalTranscript &&
            envelope.message_type != ipc::MessageType::SnippetExpansion &&
            envelope.message_type != ipc::MessageType::ToggleInput) {
          continue;
        }
        handler_(std::move(envelope));
      }

      CancelIoEx(pipe, nullptr);
      CloseHandle(pipe);
    }
  } catch (...) {
    // IPC is optional. Any worker failure leaves native typing operational.
  }
}

PipeClient::FocusSnapshot PipeClient::Snapshot() const noexcept {
  std::lock_guard lock(state_mutex_);
  return FocusSnapshot{
      .focused = focused_,
      .focus_generation = focus_generation_,
      .version = state_version_,
  };
}

bool PipeClient::WaitForFocused(FocusSnapshot& snapshot) noexcept {
  while (true) {
    snapshot = Snapshot();
    if (snapshot.focused) {
      ResetEvent(state_event_);
      if (IsCurrent(snapshot)) {
        return true;
      }
      continue;
    }

    const std::array<HANDLE, 2> handles{stop_event_, state_event_};
    const DWORD wait = WaitForMultipleObjects(
        static_cast<DWORD>(handles.size()), handles.data(), FALSE, INFINITE);
    if (wait == WAIT_OBJECT_0) {
      return false;
    }
    if (wait == WAIT_OBJECT_0 + 1) {
      ResetEvent(state_event_);
      continue;
    }
    return false;
  }
}

HANDLE PipeClient::Connect(const FocusSnapshot& snapshot) noexcept {
  while (IsCurrent(snapshot) &&
         WaitForSingleObject(stop_event_, 0) != WAIT_OBJECT_0) {
    HANDLE pipe = CreateFileW(
        full_pipe_name_.c_str(), GENERIC_READ | GENERIC_WRITE,
        0, nullptr, OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OVERLAPPED, nullptr);
    if (pipe != INVALID_HANDLE_VALUE) {
      DWORD mode = PIPE_READMODE_BYTE;
      if (SetNamedPipeHandleState(pipe, &mode, nullptr, nullptr)) {
        return pipe;
      }
      CloseHandle(pipe);
    } else {
      const DWORD error = GetLastError();
      if (error != ERROR_FILE_NOT_FOUND && error != ERROR_PIPE_BUSY) {
        return INVALID_HANDLE_VALUE;
      }
    }

    const std::array<HANDLE, 2> handles{stop_event_, state_event_};
    const DWORD wait = WaitForMultipleObjects(
        static_cast<DWORD>(handles.size()), handles.data(), FALSE,
        kRetryMilliseconds);
    if (wait == WAIT_OBJECT_0 || wait == WAIT_FAILED) {
      return INVALID_HANDLE_VALUE;
    }
    if (wait == WAIT_OBJECT_0 + 1) {
      ResetEvent(state_event_);
      return INVALID_HANDLE_VALUE;
    }
  }
  return INVALID_HANDLE_VALUE;
}

bool PipeClient::SendHello(HANDLE pipe,
                           const FocusSnapshot& snapshot) noexcept {
  ipc::Envelope hello{
      .message_type = ipc::MessageType::Hello,
      .flags = 0,
      .session_id = session_id_,
      .focus_generation = snapshot.focus_generation,
      .payload = HelloPayload(),
  };
  const std::vector<std::uint8_t> frame = ipc::Encode(hello);
  if (frame.empty()) {
    return false;
  }
  std::vector<std::uint8_t> writable(frame.begin(), frame.end());
  return TransferExact(pipe, true, writable) == TransferResult::Success;
}

PipeClient::TransferResult PipeClient::ReceiveEnvelope(
    HANDLE pipe,
    const FocusSnapshot& snapshot,
    ipc::Envelope& envelope) noexcept {
  std::array<std::uint8_t, ipc::kHeaderSize> header{};
  TransferResult result = TransferExact(pipe, false, header);
  if (result != TransferResult::Success) {
    return result;
  }
  if (!IsCurrent(snapshot)) {
    return TransferResult::StateChanged;
  }

  const std::uint32_t payload_length = ReadLittleEndian32(header, 10);
  if (payload_length > ipc::kMaximumPayloadBytes) {
    return TransferResult::Disconnected;
  }

  std::vector<std::uint8_t> frame(header.begin(), header.end());
  frame.resize(ipc::kHeaderSize + payload_length);
  if (payload_length != 0) {
    result = TransferExact(
        pipe, false,
        std::span<std::uint8_t>(frame).subspan(ipc::kHeaderSize));
    if (result != TransferResult::Success) {
      return result;
    }
  }

  const ipc::DecodeResult decoded = ipc::Decode(frame);
  if (decoded.status != ipc::DecodeStatus::Success ||
      !decoded.envelope.has_value()) {
    return TransferResult::Disconnected;
  }
  envelope = *decoded.envelope;
  return TransferResult::Success;
}

PipeClient::TransferResult PipeClient::TransferExact(
    HANDLE pipe,
    bool write,
    std::span<std::uint8_t> buffer) noexcept {
  HANDLE io_event = CreateEventW(nullptr, TRUE, FALSE, nullptr);
  if (io_event == nullptr) {
    return TransferResult::Disconnected;
  }

  TransferResult result = TransferResult::Success;
  std::size_t offset = 0;
  while (offset < buffer.size()) {
    ResetEvent(io_event);
    OVERLAPPED overlapped{};
    overlapped.hEvent = io_event;
    DWORD transferred = 0;
    const DWORD remaining = static_cast<DWORD>(std::min<std::size_t>(
        buffer.size() - offset,
        static_cast<std::size_t>((std::numeric_limits<DWORD>::max)())));
    BOOL started = write
        ? WriteFile(pipe, buffer.data() + offset, remaining,
                    &transferred, &overlapped)
        : ReadFile(pipe, buffer.data() + offset, remaining,
                   &transferred, &overlapped);
    if (!started && GetLastError() != ERROR_IO_PENDING) {
      result = TransferResult::Disconnected;
      break;
    }

    if (!started) {
      const std::array<HANDLE, 3> handles{
          stop_event_, state_event_, io_event};
      const DWORD wait = WaitForMultipleObjects(
          static_cast<DWORD>(handles.size()), handles.data(), FALSE,
          kPipeTimeoutMilliseconds);
      if (wait == WAIT_OBJECT_0 || wait == WAIT_FAILED) {
        CancelIoEx(pipe, &overlapped);
        GetOverlappedResult(pipe, &overlapped, &transferred, TRUE);
        result = TransferResult::Stopped;
        break;
      }
      if (wait == WAIT_OBJECT_0 + 1) {
        CancelIoEx(pipe, &overlapped);
        GetOverlappedResult(pipe, &overlapped, &transferred, TRUE);
        result = TransferResult::StateChanged;
        break;
      }
      if (wait != WAIT_OBJECT_0 + 2 ||
          !GetOverlappedResult(pipe, &overlapped, &transferred, FALSE)) {
        CancelIoEx(pipe, &overlapped);
        result = TransferResult::Disconnected;
        break;
      }
    }

    if (transferred == 0) {
      result = TransferResult::Disconnected;
      break;
    }
    offset += transferred;
  }

  CloseHandle(io_event);
  return result;
}

bool PipeClient::IsCurrent(const FocusSnapshot& snapshot) const noexcept {
  const FocusSnapshot current = Snapshot();
  return current.focused && current.version == snapshot.version &&
         current.focus_generation == snapshot.focus_generation;
}

}  // namespace keyina::tsf
