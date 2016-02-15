using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework.Audio;
using Terraria.ModLoader.IO;
using System.IO;

namespace Terraria.ModLoader
{
	public class SoundEffectWrapper
	{
		string cachePath;
		private SoundEffect soundEffect;
		DynamicSoundEffectInstance dynamicSound;
		int position;
		int count;
		byte[] byteArray;
		BinaryReader reader;
		long startPos;
		int dataSize;

		public SoundEffectWrapper(SoundEffect soundEffect)
		{
			this.soundEffect = soundEffect;
		}

		public SoundEffectWrapper(string cachePath)
		{
			this.cachePath = cachePath;
		}

		public bool SoundEffectExists()
		{
			return soundEffect != null;
		}

		internal SoundEffect GetSoundEffect()
		{
			return soundEffect;
		}

		internal SoundEffectInstance GetSoundEffectInstance()
		{
			Stream stream = WAVCacheIO.GetWavStream(cachePath);
			reader = new BinaryReader(stream);
			int chunkID = reader.ReadInt32();
			int fileSize = reader.ReadInt32();
			int riffType = reader.ReadInt32();
			int fmtID = reader.ReadInt32();
			int fmtSize = reader.ReadInt32();
			int fmtCode = reader.ReadInt16();
			int channels = reader.ReadInt16();
			int sampleRate = reader.ReadInt32();
			int fmtAvgBPS = reader.ReadInt32();
			int fmtBlockAlign = reader.ReadInt16();
			int bitDepth = reader.ReadInt16();
			if (fmtSize == 18)
			{
				// Read any extra values
				int fmtExtraSize = reader.ReadInt16();
				reader.ReadBytes(fmtExtraSize);
			}
			int dataID = reader.ReadInt32();
			dataSize = reader.ReadInt32();
			startPos = reader.BaseStream.Position;

			//byteArray = reader.ReadBytes(dataSize);

			dynamicSound = new DynamicSoundEffectInstance(sampleRate, (AudioChannels)channels);

			count = dynamicSound.GetSampleSizeInBytes(TimeSpan.FromMilliseconds(100));
			byteArray = new byte[count];
			dynamicSound.BufferNeeded += new EventHandler<EventArgs>(DynamicSound_BufferNeeded);

			return dynamicSound;
		}

		void DynamicSound_BufferNeeded(object sender, EventArgs e)
		{
			int read = reader.Read(byteArray, 0, count);

			dynamicSound.SubmitBuffer(byteArray, 0, read / 2);
			dynamicSound.SubmitBuffer(byteArray, 0 + read / 2, read / 2);

			if (reader.BaseStream.Position + count >= reader.BaseStream.Length)
			{
				reader.BaseStream.Position = startPos;
			}
		}
	}
}
