using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Speech.AudioFormat;
using System.Speech.Recognition;
using System.Speech.Synthesis;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace SpeechBench
{
    public static class SapiBridge
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        private static SpeechAudioFormatInfo Format()
        {
            return new SpeechAudioFormatInfo(16000, AudioBitsPerSample.Sixteen, AudioChannel.Mono);
        }

        public static void Run(string mode, string input, string output, int repeats)
        {
            var result = new Dictionary<string, object>();
            result["system_speech_assembly"] = typeof(SpeechRecognitionEngine).Assembly.FullName;
            result["clr_version"] = Environment.Version.ToString();
            var installed = SpeechRecognitionEngine.InstalledRecognizers();
            result["installed_recognizers"] = installed.Select(r => new {
                id = r.Id, name = r.Name, culture = r.Culture.Name, description = r.Description
            }).ToArray();
            using (var synth = new SpeechSynthesizer())
            {
                result["installed_voices"] = synth.GetInstalledVoices().Select(v => new {
                    name = v.VoiceInfo.Name, culture = v.VoiceInfo.Culture.Name,
                    id = v.VoiceInfo.Id, enabled = v.Enabled
                }).ToArray();
            }
            try
            {
                if (mode == "synthesize") Synthesize(input, result);
                if (mode == "recognize") Recognize(input, repeats, result);
                if (!result.ContainsKey("status")) result["status"] = "ok";
            }
            catch (Exception ex)
            {
                result["status"] = "unavailable";
                result["error"] = ex.ToString();
            }
            File.WriteAllText(output, Json.Serialize(result), new UTF8Encoding(false));
        }

        private static void Synthesize(string input, Dictionary<string, object> result)
        {
            var jobs = Json.Deserialize<List<Dictionary<string, object>>>(File.ReadAllText(input));
            using (var synth = new SpeechSynthesizer())
            {
                synth.Rate = 0;
                foreach (var job in jobs)
                {
                    synth.SelectVoice((string)job["voice"]);
                    using (var audio = new MemoryStream())
                    {
                        // Raw PCM is written only to memory, never to a playback device.
                        synth.SetOutputToAudioStream(audio, Format());
                        synth.Speak((string)job["text"]);
                        synth.SetOutputToNull();
                        File.WriteAllBytes((string)job["pcm_path"], audio.ToArray());
                    }
                }
            }
            result["synthesized_count"] = jobs.Count;
        }

        private static string Decode(SpeechRecognitionEngine engine, byte[] wav)
        {
            var parts = new List<string>();
            Exception failure = null;
            using (var stream = new MemoryStream(wav, false))
            using (var done = new AutoResetEvent(false))
            {
                EventHandler<SpeechRecognizedEventArgs> recognized = (sender, args) => {
                    if (!String.IsNullOrWhiteSpace(args.Result.Text)) parts.Add(args.Result.Text);
                };
                EventHandler<RecognizeCompletedEventArgs> completed = (sender, args) => {
                    failure = args.Error;
                    done.Set();
                };
                engine.SpeechRecognized += recognized;
                engine.RecognizeCompleted += completed;
                try
                {
                    engine.SetInputToWaveStream(stream);
                    engine.RecognizeAsync(RecognizeMode.Multiple);
                    if (!done.WaitOne(TimeSpan.FromSeconds(60)))
                    {
                        engine.RecognizeAsyncCancel();
                        done.WaitOne(TimeSpan.FromSeconds(5));
                        throw new TimeoutException("File-only dictation exceeded sixty seconds.");
                    }
                    if (failure != null) throw failure;
                }
                finally
                {
                    engine.SpeechRecognized -= recognized;
                    engine.RecognizeCompleted -= completed;
                    engine.SetInputToNull();
                }
            }
            return String.Join(" ", parts);
        }

        private static void Recognize(string input, int repeats, Dictionary<string, object> result)
        {
            var selected = SpeechRecognitionEngine.InstalledRecognizers()
                .FirstOrDefault(r => r.Culture.Name == "en-US");
            if (selected == null)
            {
                result["status"] = "unavailable";
                result["error"] = "No installed en-US System.Speech dictation recognizer; nothing installed.";
                return;
            }
            var cases = Json.Deserialize<List<Dictionary<string, object>>>(File.ReadAllText(input));
            var audio = cases.Select(c => File.ReadAllBytes((string)c["path"])).ToArray();
            var load = Stopwatch.StartNew();
            using (var engine = new SpeechRecognitionEngine(selected))
            {
                engine.LoadGrammar(new DictationGrammar());
                engine.InitialSilenceTimeout = TimeSpan.FromSeconds(5);
                engine.BabbleTimeout = TimeSpan.FromSeconds(15);
                engine.EndSilenceTimeout = TimeSpan.FromMilliseconds(500);
                engine.EndSilenceTimeoutAmbiguous = TimeSpan.FromMilliseconds(500);
                try
                {
                    engine.UpdateRecognizerSetting("AdaptationOn", 0);
                    result["adaptation"] = "disabled";
                }
                catch (Exception ex) { result["adaptation"] = "not disabled: " + ex.Message; }
                load.Stop();
                result["cold_load_seconds"] = load.Elapsed.TotalSeconds;
                result["recognizer_id"] = selected.Id;
                var warmup = Stopwatch.StartNew();
                Decode(engine, audio[0]);
                warmup.Stop();
                result["warmup_seconds"] = warmup.Elapsed.TotalSeconds;
                var rows = new List<object>();
                for (int repeat = 0; repeat < repeats; repeat++)
                {
                    for (int i = 0; i < cases.Count; i++)
                    {
                        var timer = Stopwatch.StartNew();
                        string text = Decode(engine, audio[i]);
                        timer.Stop();
                        rows.Add(new {
                            id = (string)cases[i]["id"], repeat = repeat,
                            text = text, decode_seconds = timer.Elapsed.TotalSeconds
                        });
                    }
                }
                result["rows"] = rows;
                result["status"] = "ok";
            }
        }
    }
}
