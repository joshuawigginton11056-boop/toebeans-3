using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Farm.EditorTools
{
    /// <summary>
    /// Synthesises the duck quacks as WAV assets.
    ///
    /// Generated rather than sourced, for the same reason the terrain, the track, the volcano and
    /// now the barn are: a file you can only replace is a dead end, and a sound checked into a
    /// game repository is a licence question somebody has to answer later. Four lines of DSP is a
    /// quack you can retune.
    ///
    ///     Tools > Toebeans > Farm > Bake Duck Quacks
    ///
    /// What actually makes it sound like a duck, in order of how much each matters:
    ///
    /// **The formants.** A quack is a buzzy source pushed through two strong resonances around
    /// 900 Hz and 2.1 kHz. Those two peaks are the entire difference between "duck" and "kazoo" —
    /// far more than the pitch, which people are surprisingly bad at identifying.
    ///
    /// **The downward pitch sweep.** Every quack falls, by roughly a fourth, over its length. A
    /// flat one reads as a horn.
    ///
    /// **The rasp.** The source is a sawtooth driven into a soft clip, not a sine. A duck's
    /// syrinx is not a clean oscillator and a clean oscillator does not sound like one.
    ///
    /// **The envelope.** Very fast attack, a hump, and a shorter second hump — the "qu" and the
    /// "ack". A single decaying blob is a goose.
    ///
    /// Four variants are baked so a raft of ducks does not sound like one duck heard four times;
    /// <see cref="PondDuck"/> also jitters the pitch per play on top of that.
    /// </summary>
    public static class FarmQuackBaker
    {
        public const string AudioDir = "Assets/Farm/Audio";
        public const int Variants = 4;
        const int SampleRate = 22050;

        [MenuItem("Tools/Toebeans/Farm/Bake Duck Quacks")]
        public static void Run()
        {
            int made = Bake(force: true);
            AssetDatabase.Refresh();
            Debug.Log($"Farm: baked {made} duck quacks into {AudioDir}.");
        }

        /// <summary>Bakes any missing clips. Returns how many files were written.</summary>
        public static int Bake(bool force)
        {
            Directory.CreateDirectory(AudioDir);

            int written = 0;
            for (int i = 0; i < Variants; i++)
            {
                string path = $"{AudioDir}/Quack_{i + 1}.wav";
                if (!force && File.Exists(path)) continue;

                File.WriteAllBytes(path, Wav(Render(seed: 9001 + i * 37)));
                written++;
            }

            if (written > 0) AssetDatabase.Refresh();

            for (int i = 0; i < Variants; i++)
            {
                var importer = AssetImporter.GetAtPath($"{AudioDir}/Quack_{i + 1}.wav") as AudioImporter;
                if (importer == null) continue;

                AudioImporterSampleSettings s = importer.defaultSampleSettings;
                // Decompressed on load and preloaded: these are a third of a second each and they
                // have to start on the frame a kart goes past. A streamed or compressed-in-memory
                // clip adds a decode hitch to exactly the moment that is supposed to feel instant.
                //
                // preloadAudioData lives on the sample settings, not on the importer — it moved
                // there when it became a per-platform setting, and the importer-level property is
                // obsolete in Unity 6.
                s.loadType = AudioClipLoadType.DecompressOnLoad;
                s.compressionFormat = AudioCompressionFormat.PCM;
                s.preloadAudioData = true;
                importer.defaultSampleSettings = s;
                importer.forceToMono = true;
                importer.SaveAndReimport();
            }

            return written;
        }

        // ------------------------------------------------------------------ synthesis

        static float[] Render(int seed)
        {
            var rng = new System.Random(seed);
            double Range(double a, double b) { return a + (b - a) * rng.NextDouble(); }

            double length = Range(0.26, 0.38);
            int n = (int)(length * SampleRate);
            var buffer = new float[n];

            double f0 = Range(430.0, 520.0);       // where the sweep starts
            double f1 = Range(240.0, 300.0);       // and where it lands
            double formant1 = Range(820.0, 1010.0);
            double formant2 = Range(1900.0, 2350.0);
            double drive = Range(2.4, 3.8);

            var band1 = new Biquad(formant1, Range(5.0, 8.0), SampleRate);
            var band2 = new Biquad(formant2, Range(4.0, 7.0), SampleRate);

            double phase = 0.0;
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)SampleRate;
                double u = t / length;

                // Exponential sweep, so the fall sounds even rather than crowding at the end the
                // way a linear one does. Pitch is perceived logarithmically; a linear sweep spends
                // most of its time in the bottom third of the interval.
                double freq = f0 * Math.Pow(f1 / f0, u);

                phase += freq / SampleRate;
                phase -= Math.Floor(phase);

                // Sawtooth plus a little breath. The noise is what stops the tail of the clip
                // ringing like a filtered tone once the envelope has taken the level down.
                double source = 2.0 * (phase - 0.5);
                source += (rng.NextDouble() * 2.0 - 1.0) * 0.09;

                double voiced = band1.Process(source) * 1.0 + band2.Process(source) * 0.75;
                voiced += source * 0.18;   // a little dry signal back in, or it sounds underwater

                double shaped = Math.Tanh(voiced * drive);
                buffer[i] = (float)(shaped * Envelope(u));
            }

            Normalise(buffer, 0.82f);
            return buffer;
        }

        /// <summary>
        /// The "qu-ack": a hard attack into a main hump, then a shorter second one.
        ///
        /// The attack is two milliseconds. Any slower and the clip loses the click at the front
        /// that makes it read as a call rather than as a swell — but not zero, because a waveform
        /// that starts at full amplitude puts a DC step in the speaker.
        /// </summary>
        static double Envelope(double u)
        {
            double attack = Math.Min(1.0, u / 0.02);
            double tail = 1.0 - Math.Min(1.0, Math.Max(0.0, (u - 0.86) / 0.14));

            double first = Math.Exp(-Math.Pow((u - 0.20) / 0.17, 2.0));
            double second = Math.Exp(-Math.Pow((u - 0.62) / 0.20, 2.0)) * 0.72;

            return attack * tail * Math.Min(1.0, first + second);
        }

        static void Normalise(float[] buffer, float peak)
        {
            float loudest = 0f;
            for (int i = 0; i < buffer.Length; i++) loudest = Mathf.Max(loudest, Mathf.Abs(buffer[i]));
            if (loudest < 1e-6f) return;

            float gain = peak / loudest;
            for (int i = 0; i < buffer.Length; i++) buffer[i] *= gain;
        }

        /// <summary>A resonant band-pass. Two of these in parallel are the whole duck.</summary>
        struct Biquad
        {
            readonly double _b0, _b1, _b2, _a1, _a2;
            double _x1, _x2, _y1, _y2;

            public Biquad(double frequency, double q, int sampleRate)
            {
                // RBJ cookbook constant-skirt band-pass.
                double w0 = 2.0 * Math.PI * frequency / sampleRate;
                double alpha = Math.Sin(w0) / (2.0 * q);
                double a0 = 1.0 + alpha;

                _b0 = alpha / a0;
                _b1 = 0.0;
                _b2 = -alpha / a0;
                _a1 = -2.0 * Math.Cos(w0) / a0;
                _a2 = (1.0 - alpha) / a0;
                _x1 = _x2 = _y1 = _y2 = 0.0;
            }

            public double Process(double x)
            {
                double y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
                _x2 = _x1; _x1 = x;
                _y2 = _y1; _y1 = y;
                return y;
            }
        }

        // ------------------------------------------------------------------ wav

        /// <summary>16-bit mono PCM. The smallest thing Unity imports without an opinion.</summary>
        static byte[] Wav(float[] samples)
        {
            using (var stream = new MemoryStream())
            using (var w = new BinaryWriter(stream))
            {
                int dataBytes = samples.Length * 2;

                w.Write(new[] { 'R', 'I', 'F', 'F' });
                w.Write(36 + dataBytes);
                w.Write(new[] { 'W', 'A', 'V', 'E' });

                w.Write(new[] { 'f', 'm', 't', ' ' });
                w.Write(16);                       // PCM header size
                w.Write((short)1);                 // PCM
                w.Write((short)1);                 // mono
                w.Write(SampleRate);
                w.Write(SampleRate * 2);           // byte rate
                w.Write((short)2);                 // block align
                w.Write((short)16);                // bits per sample

                w.Write(new[] { 'd', 'a', 't', 'a' });
                w.Write(dataBytes);
                foreach (float s in samples)
                {
                    w.Write((short)(Mathf.Clamp(s, -1f, 1f) * short.MaxValue));
                }

                w.Flush();
                return stream.ToArray();
            }
        }
    }
}
