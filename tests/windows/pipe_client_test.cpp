#include <windows.h>

#include <array>
#include <atomic>
#include <chrono>
#include <iostream>
#include <mutex>
#include <string>
#include <thread>
#include <vector>

#include <keyina/ipc_protocol.h>
#include <keyina/tsf/pipe_client.h>

namespace {

using namespace std::chrono_literals;

int Fail(const char* message) {
  std::cerr << message << '\n';
  return 1;
}

bool ReadExact(HANDLE pipe, std::span<std::uint8_t> destination) {
  std::size_t offset = 0;
  while (offset < destination.size()) {
    DWORD read = 0;
    if (!ReadFile(pipe, destination.data() + offset,
                  static_cast<DWORD>(destination.size() - offset),
                  &read, nullptr) || read == 0) {
      return false;
    }
    offset += read;
  }
  return true;
}

bool WriteExact(HANDLE pipe, std::span<const std::uint8_t> source) {
  std::size_t offset = 0;
  while (offset < source.size()) {
    DWORD written = 0;
    if (!WriteFile(pipe, source.data() + offset,
                   static_cast<DWORD>(source.size() - offset),
                   &written, nullptr) || written == 0) {
      return false;
    }
    offset += written;
  }
  return true;
}

std::optional<keyina::ipc::Envelope> ReadEnvelope(HANDLE pipe) {
  std::array<std::uint8_t, keyina::ipc::kHeaderSize> header{};
  if (!ReadExact(pipe, header)) {
    return std::nullopt;
  }
  const auto preliminary = keyina::ipc::Decode(header);
  if (preliminary.status == keyina::ipc::DecodeStatus::Invalid) {
    return std::nullopt;
  }

  const std::uint32_t payload_length =
      static_cast<std::uint32_t>(header[10]) |
      (static_cast<std::uint32_t>(header[11]) << 8U) |
      (static_cast<std::uint32_t>(header[12]) << 16U) |
      (static_cast<std::uint32_t>(header[13]) << 24U);
  std::vector<std::uint8_t> frame(header.begin(), header.end());
  frame.resize(keyina::ipc::kHeaderSize + payload_length);
  if (payload_length != 0 &&
      !ReadExact(pipe, std::span<std::uint8_t>(frame).subspan(
                           keyina::ipc::kHeaderSize))) {
    return std::nullopt;
  }
  const auto decoded = keyina::ipc::Decode(frame);
  return decoded.status == keyina::ipc::DecodeStatus::Success
             ? decoded.envelope
             : std::nullopt;
}

std::wstring UniquePipeName() {
  GUID guid{};
  if (FAILED(CoCreateGuid(&guid))) {
    return {};
  }
  std::array<wchar_t, 40> text{};
  if (StringFromGUID2(guid, text.data(), static_cast<int>(text.size())) <= 1) {
    return {};
  }
  return L"Keyina.Tests.PipeClient." + std::wstring(text.data());
}

}  // namespace

int wmain() {
  const std::wstring pipe_name = UniquePipeName();
  if (pipe_name.empty()) {
    return Fail("could not generate a unique pipe name");
  }
  const std::wstring full_pipe = L"\\\\.\\pipe\\" + pipe_name;

  HANDLE server = CreateNamedPipeW(
      full_pipe.c_str(), PIPE_ACCESS_DUPLEX,
      PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT,
      1, static_cast<DWORD>(keyina::ipc::kMaximumFrameBytes),
      static_cast<DWORD>(keyina::ipc::kMaximumFrameBytes), 0, nullptr);
  if (server == INVALID_HANDLE_VALUE) {
    return Fail("CreateNamedPipeW failed");
  }

  std::atomic<bool> server_ok{false};
  std::thread server_thread([&] {
    if (!ConnectNamedPipe(server, nullptr) &&
        GetLastError() != ERROR_PIPE_CONNECTED) {
      return;
    }
    const auto hello = ReadEnvelope(server);
    if (!hello.has_value() ||
        hello->message_type != keyina::ipc::MessageType::Hello ||
        hello->focus_generation != 7 ||
        hello->payload.find("cap=external_text") == std::string::npos) {
      return;
    }

    keyina::ipc::Envelope final{
        .message_type = keyina::ipc::MessageType::FinalTranscript,
        .flags = 0,
        .session_id = hello->session_id,
        .focus_generation = 7,
        .payload = "xin chào",
    };
    const auto frame = keyina::ipc::Encode(final);
    if (frame.empty() || !WriteExact(server, frame)) {
      return;
    }
    FlushFileBuffers(server);
    server_ok.store(true, std::memory_order_release);
  });

  std::mutex callback_mutex;
  std::vector<keyina::ipc::Envelope> received;
  keyina::ipc::SessionId session_id{};
  for (std::size_t index = 0; index < session_id.bytes.size(); ++index) {
    session_id.bytes[index] = static_cast<std::uint8_t>(index + 1);
  }

  keyina::tsf::PipeClient client(
      pipe_name, session_id,
      [&](keyina::ipc::Envelope envelope) {
        std::lock_guard lock(callback_mutex);
        received.push_back(std::move(envelope));
      });
  if (!client.Start()) {
    DisconnectNamedPipe(server);
    CloseHandle(server);
    server_thread.join();
    return Fail("PipeClient::Start failed");
  }
  client.SetFocused(true, 7);

  const auto deadline = std::chrono::steady_clock::now() + 3s;
  while (std::chrono::steady_clock::now() < deadline) {
    {
      std::lock_guard lock(callback_mutex);
      if (!received.empty()) {
        break;
      }
    }
    Sleep(10);
  }

  client.SetFocused(false, 8);
  client.Stop();
  server_thread.join();
  DisconnectNamedPipe(server);
  CloseHandle(server);

  if (!server_ok.load(std::memory_order_acquire)) {
    return Fail("server did not observe a valid Hello and send final text");
  }
  std::lock_guard lock(callback_mutex);
  if (received.size() != 1 ||
      received[0].message_type != keyina::ipc::MessageType::FinalTranscript ||
      received[0].payload != "xin chào" ||
      received[0].focus_generation != 7) {
    return Fail("pipe client did not deliver the expected envelope");
  }
  return 0;
}
