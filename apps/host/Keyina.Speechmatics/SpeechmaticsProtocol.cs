using System.Buffers;
using System.Text.Json;

namespace Keyina.Speechmatics;

public static class SpeechmaticsProtocol
{
    public static byte[] CreateStartRecognition(SpeechmaticsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        var buffer = new ArrayBufferWriter<byte>(256);
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("message", "StartRecognition");

            writer.WriteStartObject("audio_format");
            writer.WriteString("type", "raw");
            writer.WriteString("encoding", "pcm_s16le");
            writer.WriteNumber("sample_rate", options.SampleRate);
            writer.WriteEndObject();

            writer.WriteStartObject("transcription_config");
            writer.WriteString("language", options.Language);
            writer.WriteString("model", options.Model);
            writer.WriteNumber("max_delay", options.MaxDelaySeconds);
            writer.WriteString("max_delay_mode", options.MaxDelayMode);
            writer.WriteBoolean("enable_partials", options.EnablePartials);
            writer.WriteStartObject("conversation_config");
            writer.WriteNumber(
                "end_of_utterance_silence_trigger",
                options.EndOfUtteranceSilenceTriggerSeconds);
            writer.WriteEndObject();
            writer.WriteEndObject();

            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    public static byte[] CreateEndOfStream(int lastSequenceNumber)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(lastSequenceNumber);

        var buffer = new ArrayBufferWriter<byte>(64);
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("message", "EndOfStream");
            writer.WriteNumber("last_seq_no", lastSequenceNumber);
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    public static SpeechEvent ParseServerMessage(ReadOnlySpan<byte> utf8Json)
    {
        try
        {
            var reader = new Utf8JsonReader(utf8Json, isFinalBlock: true, state: default);
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new SpeechmaticsProtocolException("Speechmatics message must be a JSON object.");
            }

            var message = RequiredString(root, "message");
            return message switch
            {
                "RecognitionStarted" => new SpeechEvent
                {
                    Kind = SpeechEventKind.RecognitionStarted,
                    SessionId = RequiredString(root, "id"),
                },
                "AudioAdded" => new SpeechEvent
                {
                    Kind = SpeechEventKind.AudioAdded,
                    SequenceNumber = RequiredInt32(root, "seq_no"),
                },
                "AddPartialTranscript" => ParseTranscript(root, SpeechEventKind.PartialTranscript),
                "AddTranscript" => ParseTranscript(root, SpeechEventKind.FinalTranscript),
                "EndOfTranscript" => new SpeechEvent { Kind = SpeechEventKind.EndOfTranscript },
                "Error" => ParseProviderStatus(root, SpeechEventKind.ProviderError),
                "Warning" => ParseProviderStatus(root, SpeechEventKind.ProviderWarning),
                "Info" => ParseProviderStatus(root, SpeechEventKind.ProviderInfo),
                _ => new SpeechEvent
                {
                    Kind = SpeechEventKind.Unknown,
                    ProviderType = message,
                },
            };
        }
        catch (SpeechmaticsProtocolException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new SpeechmaticsProtocolException("Speechmatics returned malformed JSON.", exception);
        }
    }

    private static SpeechEvent ParseTranscript(JsonElement root, SpeechEventKind kind)
    {
        if (!root.TryGetProperty("metadata", out var metadata) || metadata.ValueKind != JsonValueKind.Object)
        {
            throw new SpeechmaticsProtocolException("Transcript message is missing metadata.");
        }

        return new SpeechEvent
        {
            Kind = kind,
            Text = RequiredStringAllowEmpty(metadata, "transcript"),
            StartTimeSeconds = RequiredDouble(metadata, "start_time"),
            EndTimeSeconds = RequiredDouble(metadata, "end_time"),
        };
    }

    private static SpeechEvent ParseProviderStatus(JsonElement root, SpeechEventKind kind)
    {
        var providerType = OptionalString(root, "type");
        var reason = OptionalString(root, "reason");
        if (kind == SpeechEventKind.ProviderError && string.IsNullOrWhiteSpace(providerType))
        {
            throw new SpeechmaticsProtocolException("Provider error is missing its type.");
        }

        return new SpeechEvent
        {
            Kind = kind,
            ProviderType = providerType,
            Reason = reason,
        };
    }

    private static string RequiredString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            throw new SpeechmaticsProtocolException($"Speechmatics message is missing string property '{propertyName}'.");
        }

        var value = property.GetString();
        if (string.IsNullOrEmpty(value))
        {
            throw new SpeechmaticsProtocolException($"Speechmatics property '{propertyName}' cannot be empty.");
        }

        return value;
    }

    private static string RequiredStringAllowEmpty(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            throw new SpeechmaticsProtocolException(
                $"Speechmatics message is missing string property '{propertyName}'.");
        }

        return property.GetString() ?? string.Empty;
    }

    private static string? OptionalString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            throw new SpeechmaticsProtocolException($"Speechmatics property '{propertyName}' must be a string.");
        }

        return property.GetString();
    }

    private static int RequiredInt32(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || !property.TryGetInt32(out var value) || value < 0)
        {
            throw new SpeechmaticsProtocolException($"Speechmatics property '{propertyName}' must be a non-negative integer.");
        }

        return value;
    }

    private static double RequiredDouble(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || !property.TryGetDouble(out var value) || !double.IsFinite(value) || value < 0)
        {
            throw new SpeechmaticsProtocolException($"Speechmatics property '{propertyName}' must be a non-negative number.");
        }

        return value;
    }
}
