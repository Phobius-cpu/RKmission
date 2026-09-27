using AOSharp.Core.UI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace MaliMissionRoller2
{
    public class Midi
    {
        [DllImport("winmm.dll")]
        static extern int mciSendString(string command, StringBuilder buffer, int bufferSize, IntPtr hwndCallback);

        public static Dictionary<string, bool> playingMidi = new Dictionary<string, bool>();
        private static bool _tearDown = false;

        public static void Play(string fileName)
        {
            Random rnd = new Random();
            string alias = $"{fileName}_{rnd.Next()}";

            if (playingMidi.ContainsKey(alias))
                return;

            playingMidi.Add(alias, false);

            Thread stoppingThread = new Thread(() =>
            {
                StartAndStopMidiWithDelay($"\"{Main.PluginDir}\\Sound\\{fileName}.wav\"", alias);
            });

            stoppingThread.Start();
        }

        public static void StopMidiFromOtherThread(string alias)
        {
            if (!playingMidi.ContainsKey(alias))
                return;

            playingMidi[alias] = true;
        }
        public static void TearDown()
        {
            _tearDown = true;
        }
        public static bool isPlaying(string alias)
        {
            return playingMidi.ContainsKey(alias);
        }

        private static void StartAndStopMidiWithDelay(string fileName, string alias)
        {
            mciSendString("open " + $"{fileName}" + " type waveaudio alias " + alias, null, 0, new IntPtr());
            mciSendString("play " + alias, null, 0, new IntPtr());

            StringBuilder result = new StringBuilder(0);
            mciSendString("set " + alias + " time format milliseconds", null, 0, new IntPtr());
            mciSendString("status " + alias + " length", result, 100, new IntPtr());

            int midiLengthInMilliseconds;
            int.TryParse(result.ToString(), out midiLengthInMilliseconds);

            Stopwatch timer = new Stopwatch();
            timer.Start();


            while ( timer.ElapsedMilliseconds < midiLengthInMilliseconds && !playingMidi[alias])
            {
                if (_tearDown)
                {
                    StopMidi(alias);
                    return;
                }
            }

            timer.Stop();

            StopMidi(alias);
        }

        private static void StopMidi(string alias)
        {
            if (!playingMidi.ContainsKey(alias))
                return;

            // Execute calls to close and stop the player, on the same thread as the play and open calls
            mciSendString("stop " + alias, null, 0, new IntPtr());
            mciSendString("close " + alias, null, 0, new IntPtr());

            playingMidi.Remove(alias);
        }
    }
}
