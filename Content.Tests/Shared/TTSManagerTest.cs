using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Diagnostics;
using Content.Server.Corvax.TTS;
using Content.Shared.Corvax.CCCVars;
using Moq;
using NUnit.Framework;
using Robust.Shared.Configuration;
using Robust.Shared.Log;

namespace Content.Tests.Shared;

[TestFixture]
public sealed class TTSManagerTest
{
    [TestCase("intercom")]
    [TestCase("robotic")]
    [TestCase("radio_headset")]
    public async Task SynthesisPreservesLongTextSelectedCharacterVoiceAndAutomaticEffect(string effect)
    {
        using var handler = new ControlledHandler();
        using var client = new HttpClient(handler);
        var system = new TTSSystem();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(TTSSystem).GetField("_ttsManager", flags)!.SetValue(system, CreateManager(client));
        var text = new string('а', 450);
        var request = (Task<byte[]>) typeof(TTSSystem).GetMethod("GenerateTTS", flags)!
            .Invoke(system, [text, "chosen character voice", effect])!;
        var query = HttpUtility.ParseQueryString(handler.RequestUris[0].Query);
        Assert.That(query["speaker"], Is.EqualTo("chosen character voice"));
        Assert.That(query["effect"], Is.EqualTo(effect));
        Assert.That(query["text"], Is.EqualTo(text + "."), "Console announcements must not use the 200-character ordinary speech limit");
        handler.Complete(0, 1);
        Assert.That(await request, Is.EqualTo(Audio(1)));
    }

    [Test]
    public async Task LocalAndHolopadRequestsShareOneSynthesisAndThenUseCache()
    {
        using var handler = new ControlledHandler();
        using var client = new HttpClient(handler);
        var manager = CreateManager(client);
        var local = manager.ConvertTextToSpeech("voice", "hello");
        var radio = manager.ConvertTextToSpeech("voice", "hello");
        Assert.That(handler.Requests.Count, Is.EqualTo(1));
        handler.Complete(0, 42);
        Assert.That(await local, Is.EqualTo(Audio(42)));
        Assert.That(await radio, Is.SameAs(await local));
        Assert.That(await manager.ConvertTextToSpeech("voice", "hello"), Is.SameAs(await local));
        Assert.That(handler.Requests.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task ResetDoesNotLetOldRoundRequestsOverwriteNewAudio()
    {
        using var handler = new ControlledHandler();
        using var client = new HttpClient(handler);
        var manager = CreateManager(client);
        var old = manager.ConvertTextToSpeech("voice", "hello");
        manager.ResetCache();
        var current = manager.ConvertTextToSpeech("voice", "hello");
        Assert.That(handler.Requests.Count, Is.EqualTo(2));
        handler.Complete(1, 2);
        await current;
        handler.Complete(0, 1);
        await old;
        Assert.That(await manager.ConvertTextToSpeech("voice", "hello"), Is.EqualTo(Audio(2)));
        Assert.That(handler.Requests.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task DifferentCharacterVoicesNeverShareAudio()
    {
        using var handler = new ControlledHandler();
        using var client = new HttpClient(handler);
        var manager = CreateManager(client);
        var first = manager.ConvertTextToSpeech("first", "announcement");
        var second = manager.ConvertTextToSpeech("second", "announcement");
        Assert.That(handler.Requests.Count, Is.EqualTo(2));
        handler.Complete(0, 1);
        handler.Complete(1, 2);
        Assert.That(await first, Is.Not.EqualTo(await second));
    }

    [Test]
    public async Task FailedSynthesisCanBeRetried()
    {
        using var handler = new ControlledHandler();
        using var client = new HttpClient(handler);
        var manager = CreateManager(client);
        var failed = manager.ConvertTextToSpeech("voice", "hello");
        handler.Complete(0, 0, HttpStatusCode.TooManyRequests);
        Assert.That(await failed, Is.Null);
        var retry = manager.ConvertTextToSpeech("voice", "hello");
        Assert.That(handler.Requests.Count, Is.EqualTo(2));
        handler.Complete(1, 1);
        Assert.That(await retry, Is.EqualTo(Audio(1)));
    }

    [Test]
    public async Task EffectsHaveSeparateCachesAndPendingRequests()
    {
        using var handler = new ControlledHandler();
        using var client = new HttpClient(handler);
        var manager = CreateManager(client);
        var local = manager.ConvertTextToSpeech("voice", "hello");
        var radio = manager.ConvertTextToSpeech("voice", "hello", "radio_headset");
        var console = manager.ConvertTextToSpeech("voice", "hello", "announce");
        Assert.That(handler.Requests.Count, Is.EqualTo(3));
        Assert.That(HttpUtility.ParseQueryString(handler.RequestUris[0].Query)["effect"], Is.Null);
        Assert.That(HttpUtility.ParseQueryString(handler.RequestUris[1].Query)["effect"], Is.EqualTo("radio_headset"));
        Assert.That(HttpUtility.ParseQueryString(handler.RequestUris[2].Query)["effect"], Is.EqualTo("announce"));
        handler.Complete(0, 1);
        handler.Complete(1, 2);
        handler.Complete(2, 3);
        await Task.WhenAll(local, radio, console);
        Assert.That(await manager.ConvertTextToSpeech("voice", "hello", "radio_headset"), Is.SameAs(await radio));
        Assert.That(await manager.ConvertTextToSpeech("voice", "hello", "announce"), Is.SameAs(await console));
        Assert.That(handler.Requests.Count, Is.EqualTo(3));
    }

    [TestCase(0)]
    [TestCase(2)]
    [TestCase(4)]
    [TestCase(-1)]
    public async Task RetryCountIsAdditionalAttemptsAndFailureIsNotCached(int retries)
    {
        using var handler = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        using var client = new HttpClient(handler);
        var manager = CreateManager(client, retries);
        Assert.That(await manager.ConvertTextToSpeech("voice", "hello"), Is.Null);
        Assert.That(handler.Count, Is.EqualTo(Math.Max(0, retries) + 1));
        Assert.That(await manager.ConvertTextToSpeech("voice", "hello"), Is.Null);
        Assert.That(handler.Count, Is.EqualTo((Math.Max(0, retries) + 1) * 2));
    }

    [TestCase("status")]
    [TestCase("empty")]
    [TestCase("json")]
    [TestCase("network")]
    [TestCase("timeout")]
    public async Task FailedRequestsRetryAndStopImmediatelyOnSuccess(string failure)
    {
        using var handler = new ScriptedHandler(attempt =>
        {
            if (attempt == 2) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Audio(7)) };
            return failure switch
            {
                "status" => new HttpResponseMessage(HttpStatusCode.TooManyRequests),
                "empty" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) },
                "json" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"error\":\"tokens exhausted\"}") },
                "network" => throw new HttpRequestException(),
                _ => throw new TaskCanceledException(),
            };
        });
        using var client = new HttpClient(handler);
        var manager = CreateManager(client, 2);
        Assert.That(await manager.ConvertTextToSpeech("voice", "hello"), Is.EqualTo(Audio(7)));
        Assert.That(handler.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task RetryWaitsForConfiguredDelayAndSharesTheWholeChain()
    {
        using var handler = new ScriptedHandler(attempt => attempt == 1
            ? new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Audio(4)) });
        using var client = new HttpClient(handler);
        var manager = CreateManager(client, 2, 0.15f);
        var timer = Stopwatch.StartNew();
        var first = manager.ConvertTextToSpeech("voice", "hello");
        var second = manager.ConvertTextToSpeech("voice", "hello");
        Assert.That(handler.Count, Is.EqualTo(1));
        Assert.That(await first, Is.SameAs(await second));
        Assert.That(timer.Elapsed.TotalSeconds, Is.GreaterThanOrEqualTo(0.13));
        Assert.That(handler.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task ResetPreventsDelayedRetryFromPreviousRound()
    {
        using var handler = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var client = new HttpClient(handler);
        var manager = CreateManager(client, 2, 0.05f);
        var request = manager.ConvertTextToSpeech("voice", "hello");
        manager.ResetCache();
        Assert.That(await request, Is.Null);
        Assert.That(handler.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task ApiQueryParametersArePreservedAndTextCannotInjectAnEffect()
    {
        using var handler = new ControlledHandler();
        using var client = new HttpClient(handler);
        var manager = CreateManager(client);
        typeof(TTSManager).GetField("_apiUrl", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(manager, "https://tts.invalid/synthesize?existing=value&effect=old");
        var request = manager.ConvertTextToSpeech("voice", "hello&effect=robotic", "radio");
        var query = HttpUtility.ParseQueryString(handler.RequestUris[0].Query);
        Assert.That(query["existing"], Is.EqualTo("value"));
        Assert.That(query["text"], Is.EqualTo("hello&effect=robotic"));
        Assert.That(query["effect"], Is.EqualTo("radio"));
        handler.Complete(0, 3);
        await request;
    }

    private static byte[] Audio(byte marker) => [(byte) 'O', (byte) 'g', (byte) 'g', (byte) 'S', marker];

    private static TTSManager CreateManager(HttpClient client, int retries = 0, float retryDelay = 0f)
    {
        var cfg = new Mock<IConfigurationManager>();
        cfg.Setup(c => c.GetCVar(CCCVars.TTSApiTimeout)).Returns(5);
        cfg.Setup(c => c.GetCVar(CCCVars.TTSRetryCount)).Returns(retries);
        cfg.Setup(c => c.GetCVar(CCCVars.TTSRetryDelay)).Returns(retryDelay);
        var manager = new TTSManager();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(TTSManager).GetField("_cfg", flags)!.SetValue(manager, cfg.Object);
        typeof(TTSManager).GetField("_sawmill", flags)!.SetValue(manager, new Mock<ISawmill>().Object);
        ((HttpClient) typeof(TTSManager).GetField("_httpClient", flags)!.GetValue(manager)!).Dispose();
        typeof(TTSManager).GetField("_httpClient", flags)!.SetValue(manager, client);
        typeof(TTSManager).GetField("_apiUrl", flags)!.SetValue(manager, "https://tts.invalid/synthesize");
        return manager;
    }

    private sealed class ControlledHandler : HttpMessageHandler
    {
        public readonly List<TaskCompletionSource<HttpResponseMessage>> Requests = new();
        public readonly List<Uri> RequestUris = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new TaskCompletionSource<HttpResponseMessage>();
            Requests.Add(response);
            RequestUris.Add(request.RequestUri!);
            return response.Task;
        }

        public void Complete(int index, byte data, HttpStatusCode status = HttpStatusCode.OK) =>
            Requests[index].SetResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(Audio(data)) });
    }

    private sealed class ScriptedHandler(Func<int, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Count;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(++Count));
    }
}
