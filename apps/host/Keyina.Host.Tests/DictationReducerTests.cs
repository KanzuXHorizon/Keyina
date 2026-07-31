using Keyina.Host.Core.Speech;

namespace Keyina.Host.Tests;

internal static class DictationReducerTests
{
    [KeyinaTest("dictation reducer follows connecting listening finalizing and inserted states")]
    private static void HappyPathTransitionsAreExplicit()
    {
        var state = DictationState.Initial;
        state = DictationReducer.Apply(state, new DictationEvent.StartRequested());
        AssertEx.Equal(DictationStatus.Connecting, state.Status);

        state = DictationReducer.Apply(state, new DictationEvent.RecognitionStarted());
        AssertEx.Equal(DictationStatus.Listening, state.Status);

        state = DictationReducer.Apply(state, new DictationEvent.PartialUpdated("xin chao"));
        AssertEx.Equal("xin chao", state.PartialText);

        state = DictationReducer.Apply(
            state,
            new DictationEvent.FinalReceived("xin chào"));
        AssertEx.Equal(1, state.FinalSegments);
        AssertEx.Equal("xin chào", state.CommittedText);
        AssertEx.Equal("", state.PartialText);

        state = DictationReducer.Apply(state, new DictationEvent.PartialUpdated("thế giới"));
        AssertEx.Equal("xin chào", state.CommittedText);
        AssertEx.Equal("thế giới", state.PartialText);

        state = DictationReducer.Apply(state, new DictationEvent.StopRequested());
        AssertEx.Equal(DictationStatus.Finalizing, state.Status);

        state = DictationReducer.Apply(state, new DictationEvent.FinalInserted());
        AssertEx.Equal(DictationStatus.Inserted, state.Status);
    }

    [KeyinaTest("dictation reducer exposes error cancelled and reset states")]
    private static void ErrorCancelAndResetAreExplicit()
    {
        var connecting = DictationReducer.Apply(
            DictationState.Initial,
            new DictationEvent.StartRequested());
        var failed = DictationReducer.Apply(connecting, new DictationEvent.Failed("network"));
        AssertEx.Equal(DictationStatus.Error, failed.Status);
        AssertEx.Equal("network", failed.ErrorCode);

        var reset = DictationReducer.Apply(failed, new DictationEvent.Reset());
        AssertEx.Equal(DictationState.Initial, reset);

        var cancelled = DictationReducer.Apply(connecting, new DictationEvent.Cancelled());
        AssertEx.Equal(DictationStatus.Cancelled, cancelled.Status);
    }

    [KeyinaTest("dictation reducer rejects invalid transitions and empty payloads")]
    private static void InvalidTransitionsAreRejected()
    {
        AssertThrows<InvalidOperationException>(() =>
            DictationReducer.Apply(DictationState.Initial, new DictationEvent.RecognitionStarted()));
        AssertThrows<InvalidOperationException>(() =>
            DictationReducer.Apply(DictationState.Initial, new DictationEvent.StopRequested()));
        AssertThrows<ArgumentException>(() =>
            DictationReducer.Apply(
                new DictationState(DictationStatus.Listening, "", "", 0, null),
                new DictationEvent.PartialUpdated("")));
        AssertThrows<ArgumentException>(() =>
            DictationReducer.Apply(
                new DictationState(DictationStatus.Listening, "", "", 0, null),
                new DictationEvent.Failed("")));
        AssertThrows<ArgumentException>(() =>
            DictationReducer.Apply(
                new DictationState(DictationStatus.Listening, "", "", 0, null),
                new DictationEvent.FinalReceived("")));
    }

    private static void AssertThrows<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}
