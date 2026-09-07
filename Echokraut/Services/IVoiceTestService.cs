using System;
using Echokraut.DataClasses;

namespace Echokraut.Services;

public interface IVoiceTestService
{
    EchokrautVoice? TestingVoice { get; }
    bool IsPlaying { get; }
    bool IsTestingVoice(EchokrautVoice voice);
    void TestVoice(EchokrautVoice voice);
    void StopVoice();
    event Action? TestStateChanged;
}
