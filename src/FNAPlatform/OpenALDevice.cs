#region License
/* FNA - XNA4 Reimplementation for Desktop Platforms
 * Copyright 2009-2016 Ethan Lee and the MonoGame Team
 *
 * Released under the Microsoft Public License.
 * See LICENSE for details.
 */
#endregion

#region VERBOSE_AL_DEBUGGING Option
// #define VERBOSE_AL_DEBUGGING
/* OpenAL does not have a function similar to ARB_debug_output. Because of this,
 * we only have alGetError to debug. In DEBUG, we call this once per frame.
 *
 * If you enable this define, we call this after every single AL operation, and
 * throw an Exception when any errors show up. This makes finding problems a lot
 * easier, but calling alGetError so often can slow things down.
 * -flibit
 */
#endregion

#region Using Statements
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using OpenTK;
using OpenTK.Audio;
using OpenTK.Audio.OpenAL;
#endregion

namespace Microsoft.Xna.Framework.Audio
{
	internal class OpenALDevice : IALDevice
	{
		#region OpenAL Buffer Container Class

		private class OpenALBuffer : IALBuffer
		{
			public uint Handle
			{
				get;
				private set;
			}

			public TimeSpan Duration
			{
				get;
				private set;
			}

			public int Channels
			{
				get;
				private set;
			}

			public int SampleRate
			{
				get;
				private set;
			}

			public OpenALBuffer(uint handle, TimeSpan duration, int channels, int sampleRate)
			{
				Handle = handle;
				Duration = duration;
				Channels = channels;
				SampleRate = sampleRate;
			}
		}

		#endregion

		#region OpenAL Source Container Class

		private class OpenALSource : IALSource
		{
			public uint Handle
			{
				get;
				private set;
			}

			public OpenALSource(uint handle)
			{
				Handle = handle;
			}
		}

		#endregion

		#region OpenAL Reverb Effect Container Class

		private class OpenALReverb : IALReverb
		{
			public uint SlotHandle
			{
				get;
				private set;
			}

			public uint EffectHandle
			{
				get;
				private set;
			}

			public OpenALReverb(uint slot, uint effect)
			{
				SlotHandle = slot;
				EffectHandle = effect;
			}
		}

		#endregion

		#region Private ALC Variables

		// OpenAL Context/Efx Handles
		private AudioContext alContext;
		private EffectsExtension efx;

		#endregion

		#region Private EFX Variables

		// OpenAL Filter Handle
		private uint INTERNAL_alFilter;

		#endregion

		#region Public Constructor

		public OpenALDevice()
		{
			string envDevice = Environment.GetEnvironmentVariable("FNA_AUDIO_DEVICE_NAME");
			if (String.IsNullOrEmpty(envDevice))
			{
				/* Be sure ALC won't explode if the variable doesn't exist.
				 * But, fail if the device name is wrong. The user needs to know
				 * if their environment variable was incorrect.
				 * -flibit
				 */
				envDevice = String.Empty;
			}

			int[] attribute = new int[0];
			alContext = new AudioContext(envDevice);

			alContext.MakeCurrent();

			float[] ori = new float[]
			{
				0.0f, 0.0f, -1.0f, 0.0f, 1.0f, 0.0f
			};
			AL.Listener(ALListenerfv.Orientation, ref ori);
			AL.Listener(ALListener3f.Position, 0.0f, 0.0f, 0.0f);
            AL.Listener(ALListener3f.Velocity, 0.0f, 0.0f, 0.0f);
			AL.Listener(ALListenerf.Gain, 1.0f);

			efx = new EffectsExtension();
			efx.GenFilter(out INTERNAL_alFilter);

			// FIXME: Remove for FNA 16.11! -flibit
			if (!AL.IsExtensionPresent("AL_SOFT_gain_clamp_ex"))
			{
				FNALoggerEXT.LogWarn("AL_SOFT_gain_clamp_ex not found!");
				FNALoggerEXT.LogWarn("Update your OpenAL Soft library!");
			}
		}

		#endregion

		#region Public Dispose Method

		public void Dispose()
		{
			efx.DeleteFilter(ref INTERNAL_alFilter);
			if (alContext != null)
			{
				alContext.Dispose();
			}
		}

		#endregion

		#region Public Update Method

		public void Update()
		{
#if DEBUG
			CheckALError();
#endif
		}

		#endregion

		#region Public Listener Methods

		public void SetMasterVolume(float volume)
		{
			/* FIXME: How to ignore listener for individual sources? -flibit
			 * AL.Listenerf(AL._GAIN, volume);
			 * Media.MediaPlayer.Queue.ActiveSong.Volume = Media.MediaPlayer.Volume;
			 */
		}

		public void SetDopplerScale(float scale)
		{
			AL.DopplerFactor(scale);
		}

		public void SetSpeedOfSound(float speed)
		{
			AL.SpeedOfSound(speed);
		}

		#endregion

		#region OpenAL Buffer Methods

		public IALBuffer GenBuffer(int sampleRate, AudioChannels channels)
		{
			uint result;
			AL.GenBuffers(1, out result);
#if VERBOSE_AL_DEBUGGING
			CheckALError();
#endif
			return new OpenALBuffer(result, TimeSpan.Zero, (int) channels, sampleRate);
		}

		public IALBuffer GenBuffer(
			byte[] data,
			uint sampleRate,
			uint channels,
			uint loopStart,
			uint loopEnd,
			bool isADPCM,
			uint formatParameter
		) {
			uint result;

			// Generate the buffer now, in case we need to perform alBuffer ops.
			AL.GenBuffers(1, out result);
#if VERBOSE_AL_DEBUGGING
			CheckALError();
#endif

			ALFormat format;
			int length = data.Length;
			if (isADPCM)
			{
				format = (channels == 2) ?
				ALFormat.StereoIma4Ext :
				ALFormat.MonoIma4Ext;
				//AL10.alBufferi(
				//    result,
				//    ALEXT.AL_UNPACK_BLOCK_ALIGNMENT_SOFT,
				//    (int)formatParameter
				//);
				throw new NotSupportedException();
            }
			else
			{
				if (formatParameter == 1)
				{
					format = (channels == 2) ?
						ALFormat.Stereo16 :
						ALFormat.Mono16;

					/* We have to perform extra data validation on
					 * PCM16 data, as the MS SoundEffect builder will
					 * leave extra bytes at the end which will confuse
					 * alBufferData and throw an AL_INVALID_VALUE.
					 * -flibit
					 */
					length &= 0x7FFFFFFE;
				}
				else
				{
					format = (channels == 2) ?
						ALFormat.Stereo8:
						ALFormat.Mono8;
				}
			}

			// Load it!
			AL.BufferData(
				(int) result,
				format,
				data,
				length,
				(int) sampleRate
			);
#if VERBOSE_AL_DEBUGGING
			CheckALError();
#endif

			// Calculate the duration now, after we've unpacked the buffer
			int bufLen, bits;
			AL.GetBuffer(result, ALGetBufferi.Size, out bufLen);
            AL.GetBuffer(result, ALGetBufferi.Bits, out bits);
			if (bufLen == 0 || bits == 0)
			{
				throw new InvalidOperationException(
					"OpenAL buffer allocation failed!"
				);
			}

			var totalFrames = bufLen /
				(bits / 8) /
				channels;

            var resultDur = TimeSpan.FromSeconds(totalFrames /
                ((double)sampleRate));

			// Set the loop points, if applicable
			if (loopStart > 0 || (loopEnd != totalFrames && loopEnd > 0))
			{
				if (AL.IsExtensionPresent("AL_LOOP_POINTS_SOFT"))
				{
     //               AL.Buffer(
					//	result,
					//	ALEXT.AL_LOOP_POINTS_SOFT,
					//	new int[]
					//	{
					//						(int) loopStart,
					//						(int) loopEnd
					//	}
					//);
                }
				else
				{
                    Trace.WriteLine($"Unable to set loop points! Not supported, possibly. From: {loopStart}. To: {loopEnd}. Total: {totalFrames}");
					throw new NotImplementedException("Looping points not implemented");
                }
			}
#if VERBOSE_AL_DEBUGGING
			CheckALError();
#endif

			// Finally.
			return new OpenALBuffer(result, resultDur, (int) channels, (int) sampleRate);
		}

		public void DeleteBuffer(IALBuffer buffer)
		{
			uint handle = (buffer as OpenALBuffer).Handle;
			AL.DeleteBuffers(1, ref handle);
#if VERBOSE_AL_DEBUGGING
			CheckALError();
#endif
		}

		public void SetBufferData(
			IALBuffer buffer,
			IntPtr data,
			int offset,
			int count
		) {
			OpenALBuffer buf = buffer as OpenALBuffer;
			AL.BufferData(
				buf.Handle,
				XNAToShort[buf.Channels],
				data + offset,
				count,
				buf.SampleRate
			);
#if VERBOSE_AL_DEBUGGING
			CheckALError();
#endif
		}

		public void SetBufferFloatData(
			IALBuffer buffer,
			IntPtr data,
			int offset,
			int count
		) {
			OpenALBuffer buf = buffer as OpenALBuffer;
			AL.BufferData(
				buf.Handle,
				XNAToFloat[buf.Channels],
				data + (offset * 4),
				count * 4,
				buf.SampleRate
			);
#if VERBOSE_AL_DEBUGGING
			CheckALError();
#endif
		}

		public IALBuffer ConvertStereoToMono(IALBuffer buffer)
		{
			OpenALBuffer buf = buffer as OpenALBuffer;
			int bufLen, bits;
			AL.GetBuffer(buf.Handle, ALGetBufferi.Size, out bufLen);
            AL.GetBuffer(buf.Handle, ALGetBufferi.Bits, out bits);
			bits /= 8;
#if VERBOSE_AL_DEBUGGING
			CheckALError();
#endif

			byte[] data = new byte[bufLen];
			GCHandle dataHandle = GCHandle.Alloc(data, GCHandleType.Pinned);
			IntPtr dataPtr = dataHandle.AddrOfPinnedObject();
			var notImplemented = true;
			if (notImplemented)
            {
                throw new NotImplementedException();
            }
			//ALEXT.alGetBufferSamplesSOFT(
			//	buf.Handle,
			//	0,
			//	bufLen / bits / 2,
			//	ALEXT.AL_STEREO_SOFT,
			//	bits == 2 ? ALEXT.AL_SHORT_SOFT : ALEXT.AL_BYTE_SOFT,
			//	dataPtr
			//);
#if VERBOSE_AL_DEBUGGING
			CheckALError();
#endif

			byte[] monoData = new byte[bufLen / 2];
			GCHandle monoHandle = GCHandle.Alloc(monoData, GCHandleType.Pinned);
			IntPtr monoPtr = monoHandle.AddrOfPinnedObject();
			unsafe
			{
				if (bits == 2)
				{
					short* src = (short*) dataPtr;
					short* dst = (short*) monoPtr;
					for (int i = 0; i < monoData.Length / 2; i += 1)
					{
						dst[i] = (short) (((int) src[0] + (int) src[1]) / 2);
						src += 2;
					}
				}
				else
				{
					sbyte* src = (sbyte*) dataPtr;
					sbyte* dst = (sbyte*) monoPtr;
					for (int i = 0; i < monoData.Length; i += 1)
					{
						dst[i] = (sbyte) (((short) src[0] + (short) src[1]) / 2);
						src += 2;
					}
				}
			}
			monoHandle.Free();
			dataHandle.Free();
			data = null;

			return GenBuffer(
				monoData,
				(uint) buf.SampleRate,
				1,
				0,
				0,
				false,
				(uint) bits - 1
			);
		}

		#endregion

		#region OpenAL Source Methods

		public IALSource GenSource()
		{
			uint result;
			AL.GenSources(1, out result);
#if VERBOSE_AL_DEBUGGING
			CheckALError();
#endif
			if (result == 0)
			{
				return null;
			}
			AL.Source(result, ALSourcef.ReferenceDistance, AudioDevice.DistanceScale);
			return new OpenALSource(result);
		}

		public IALSource GenSource(IALBuffer buffer, bool isXACT)
		{
			uint result;
			AL.GenSources(1, out result);
#if VERBOSE_AL_DEBUGGING
			CheckALError();
#endif
			if (result == 0)
			{
				return null;
			}
            AL.Source(result, ALSourcei.Buffer, (int)(buffer as OpenALBuffer).Handle);
			AL.Source(result, ALSourcef.ReferenceDistance, AudioDevice.DistanceScale);
			if (isXACT)
			{
				// FIXME: Arbitrary, but try to keep this sane! -flibit
				AL.Source(result, ALSourcef.MaxGain, 64.0f);
			}
#if VERBOSE_AL_DEBUGGING
			CheckALError();
#endif
			return new OpenALSource(result);
		}

		public void StopAndDisposeSource(IALSource source)
		{
			uint handle = (source as OpenALSource).Handle;
			AL.SourceStop(handle);
#if VERBOSE_AL_DEBUGGING
			CheckALError();
#endif
			AL.DeleteSources(1, ref handle);
#if VERBOSE_AL_DEBUGGING
			CheckALError();
#endif
		}

		public void PlaySource(IALSource source)
		{
			AL.SourcePlay((source as OpenALSource).Handle);
#if VERBOSE_AL_DEBUGGING
			CheckALError();
#endif
		}

		public void PauseSource(IALSource source)
		{
			AL.SourcePause((source as OpenALSource).Handle);
#if VERBOSE_AL_DEBUGGING
			CheckALError();
#endif
		}

		public void ResumeSource(IALSource source)
		{
			AL.SourcePlay((source as OpenALSource).Handle);
#if VERBOSE_AL_DEBUGGING
			CheckALError();
#endif
		}

		public SoundState GetSourceState(IALSource source)
		{
			var state = AL.GetSourceState((source as OpenALSource).Handle);
#if VERBOSE_AL_DEBUGGING
			CheckALError();
#endif
			if (state == ALSourceState.Playing)
			{
				return SoundState.Playing;
			}
			else if (state == ALSourceState.Paused)
			{
				return SoundState.Paused;
			}
			return SoundState.Stopped;
		}

		public void SetSourceVolume(IALSource source, float volume)
		{
			AL.Source((source as OpenALSource).Handle, ALSourcef.Gain, volume * SoundEffect.MasterVolume); // FIXME: alListener(AL_GAIN) -flibit
#if VERBOSE_AL_DEBUGGING
			CheckALError();
#endif
		}

		public void SetSourceLooped(IALSource source, bool looped)
		{
			AL.Source((source as OpenALSource).Handle, ALSourceb.Looping, looped);
#if VERBOSE_AL_DEBUGGING
			CheckALError();
#endif
		}

		public void SetSourcePan(IALSource source, float pan)
		{
			AL.Source((source as OpenALSource).Handle, ALSource3f.Position, pan, 0.0f, (float)-Math.Sqrt(1 - Math.Pow(pan, 2)));
#if VERBOSE_AL_DEBUGGING
			CheckALError();
#endif
		}

		public void SetSourcePosition(IALSource source, Vector3 pos)
		{
            AL.Source((source as OpenALSource).Handle, ALSource3f.Position, pos.X, pos.Y, pos.Z);
#if VERBOSE_AL_DEBUGGING
			CheckALError();
#endif
		}

		public void SetSourcePitch(IALSource source, float pitch, bool clamp)
		{
			/* XNA sets pitch bounds to [-1.0f, 1.0f], each end being one octave.
			 * OpenAL's AL_PITCH boundaries are (0.0f, INF).
			 * Consider the function f(x) = 2 ^ x
			 * The domain is (-INF, INF) and the range is (0, INF).
			 * 0.0f is the original pitch for XNA, 1.0f is the original pitch for OpenAL.
			 * Note that f(0) = 1, f(1) = 2, f(-1) = 0.5, and so on.
			 * XNA's pitch values are on the domain, OpenAL's are on the range.
			 * Remember: the XNA limit is arbitrarily between two octaves on the domain.
			 * To convert, we just plug XNA pitch into f(x).
			 * -flibit
			 */
			if (clamp && (pitch < -1.0f || pitch > 1.0f))
			{
				throw new IndexOutOfRangeException("XNA PITCH MUST BE WITHIN [-1.0f, 1.0f]!");
			}
			AL.Source((source as OpenALSource).Handle, ALSourcef.Pitch, (float)Math.Pow(2, pitch));
#if VERBOSE_AL_DEBUGGING
			CheckALError();
#endif
		}

		public void SetSourceReverb(IALSource source, IALReverb reverb)
		{
			AL.Source((source as OpenALSource).Handle, ALSource3i.EfxAuxiliarySendFilter, (int)(reverb as OpenALReverb).SlotHandle, 0, 0);
#if VERBOSE_AL_DEBUGGING
			CheckALError();
#endif
		}

		public void SetSourceLowPassFilter(IALSource source, float hfGain)
		{
			efx.Filter(INTERNAL_alFilter, EfxFilteri.FilterType, (int)EfxFilterType.Lowpass);
			efx.Filter(INTERNAL_alFilter, EfxFilterf.LowpassGainHF, hfGain);
			AL.Source((source as OpenALSource).Handle, ALSourcei.EfxDirectFilter,(int) INTERNAL_alFilter);
#if VERBOSE_AL_DEBUGGING
			CheckALError();
#endif
		}

		public void SetSourceHighPassFilter(IALSource source, float lfGain)
		{
            efx.Filter(INTERNAL_alFilter, EfxFilteri.FilterType, (int)EfxFilterType.Highpass);
            efx.Filter(INTERNAL_alFilter, EfxFilterf.HighpassGainLF, lfGain);
            AL.Source((source as OpenALSource).Handle, ALSourcei.EfxDirectFilter, (int)INTERNAL_alFilter);
#if VERBOSE_AL_DEBUGGING
			CheckALError();
#endif
		}

		public void SetSourceBandPassFilter(IALSource source, float hfGain, float lfGain)
		{
            efx.Filter(INTERNAL_alFilter, EfxFilteri.FilterType, (int)EfxFilterType.Bandpass);
            efx.Filter(INTERNAL_alFilter, EfxFilterf.BandpassGainHF, hfGain);
            efx.Filter(INTERNAL_alFilter, EfxFilterf.BandpassGainLF, lfGain);
            AL.Source((source as OpenALSource).Handle, ALSourcei.EfxDirectFilter, (int)INTERNAL_alFilter);
#if VERBOSE_AL_DEBUGGING
			CheckALError();
#endif
		}

		public void QueueSourceBuffer(IALSource source, IALBuffer buffer)
		{
			uint buf = (buffer as OpenALBuffer).Handle;
			AL.SourceQueueBuffers(
				(source as OpenALSource).Handle,
				1,
				ref buf
			);
#if VERBOSE_AL_DEBUGGING
			CheckALError();
#endif
		}

		public void DequeueSourceBuffers(
			IALSource source,
			int buffersToDequeue,
			Queue<IALBuffer> errorCheck
		) {
			uint[] bufs = new uint[buffersToDequeue];
			AL.SourceUnqueueBuffers(
				(source as OpenALSource).Handle,
				buffersToDequeue,
				bufs
			);
#if VERBOSE_AL_DEBUGGING
			CheckALError();
#endif
#if DEBUG
			// Error check our queuedBuffers list.
			IALBuffer[] sync = errorCheck.ToArray();
			for (int i = 0; i < buffersToDequeue; i += 1)
			{
				if (bufs[i] != (sync[i] as OpenALBuffer).Handle)
				{
					throw new InvalidOperationException("Buffer desync!");
				}
			}
#endif
		}

		public int CheckProcessedBuffers(IALSource source)
		{
			AL.GetSource((source as OpenALSource).Handle, ALGetSourcei.BuffersProcessed, out var result);
#if VERBOSE_AL_DEBUGGING
			CheckALError();
#endif
			return result;
		}

		public void GetBufferData(
			IALSource source,
			IALBuffer[] buffer,
			IntPtr samples,
			int samplesLen,
			AudioChannels channels
		) {
			int copySize1 = samplesLen / (int) channels;
			int copySize2 = 0;

			// Where are we now?
			AL.GetSource((source as OpenALSource).Handle, ALGetSourcei.SampleOffset, out var offset);

			// Is that longer than what the active buffer has left...?
			uint buf = (buffer[0] as OpenALBuffer).Handle;
			AL.GetBuffer(buf, ALGetBufferi.Size, out var len);
			len /= 2; // FIXME: Assuming 16-bit!
			len /= (int) channels;
			if (offset > len)
			{
				copySize2 = copySize1;
				copySize1 = 0;
				offset -= len;
			}
			else if (offset + copySize1 > len)
			{
				copySize2 = copySize1 - (len - offset);
				copySize1 = (len - offset);
			}

			// Copy!
			if (copySize1 > 0)
			{
				throw new NotImplementedException();
				//ALEXT.alGetBufferSamplesSOFT(
				//	buf,
				//	offset,
				//	copySize1,
				//	channels == AudioChannels.Stereo ?
				//		ALEXT.AL_STEREO_SOFT :
				//		ALEXT.AL_MONO_SOFT,
				//	ALEXT.AL_FLOAT_SOFT,
				//	samples
				//);
				//offset = 0;
			}
			if (buffer.Length > 1 && copySize2 > 0)
			{
                throw new NotImplementedException();
    //            ALEXT.alGetBufferSamplesSOFT(
				//	(buffer[1] as OpenALBuffer).Handle,
				//	0,
				//	copySize2,
				//	channels == AudioChannels.Stereo ?
				//		ALEXT.AL_STEREO_SOFT :
				//		ALEXT.AL_MONO_SOFT,
				//	ALEXT.AL_FLOAT_SOFT,
				//	samples + (copySize1 * (int) channels)
				//);
			}
		}

		#endregion

		#region OpenAL Reverb Effect Methods

		public IALReverb GenReverb(DSPParameter[] parameters)
		{
			uint slot, effect;
			efx.GenAuxiliaryEffectSlots(1, out slot);
			efx.GenEffects(1, out effect);
			// Set up the Reverb Effect
			efx.Effect(effect, EfxEffecti.EffectType, (int)EfxEffectType.EaxReverb);

			IALReverb result = new OpenALReverb(slot, effect);

			// Apply initial values
			SetReverbReflectionsDelay(result, parameters[0].Value);
			SetReverbDelay(result, parameters[1].Value);
			SetReverbPositionLeft(result, parameters[2].Value);
			SetReverbPositionRight(result, parameters[3].Value);
			SetReverbPositionLeftMatrix(result, parameters[4].Value);
			SetReverbPositionRightMatrix(result, parameters[5].Value);
			SetReverbEarlyDiffusion(result, parameters[6].Value);
			SetReverbLateDiffusion(result, parameters[7].Value);
			SetReverbLowEQGain(result, parameters[8].Value);
			SetReverbLowEQCutoff(result, parameters[9].Value);
			SetReverbHighEQGain(result, parameters[10].Value);
			SetReverbHighEQCutoff(result, parameters[11].Value);
			SetReverbRearDelay(result, parameters[12].Value);
			SetReverbRoomFilterFrequency(result, parameters[13].Value);
			SetReverbRoomFilterMain(result, parameters[14].Value);
			SetReverbRoomFilterHighFrequency(result, parameters[15].Value);
			SetReverbReflectionsGain(result, parameters[16].Value);
			SetReverbGain(result, parameters[17].Value);
			SetReverbDecayTime(result, parameters[18].Value);
			SetReverbDensity(result, parameters[19].Value);
			SetReverbRoomSize(result, parameters[20].Value);
			SetReverbWetDryMix(result, parameters[21].Value);

			// Bind the Effect to the EffectSlot. XACT will use the EffectSlot.
			efx.BindEffectToAuxiliarySlot(slot, effect);

#if VERBOSE_AL_DEBUGGING
			CheckALError();
#endif
			return result;
		}

		public void DeleteReverb(IALReverb reverb)
		{
			OpenALReverb rv = (reverb as OpenALReverb);
			uint slot = rv.SlotHandle;
			uint effect = rv.EffectHandle;
			efx.DeleteAuxiliaryEffectSlots(1, ref slot);
			efx.DeleteEffects(1, ref effect);
#if VERBOSE_AL_DEBUGGING
			CheckALError();
#endif
		}

		public void CommitReverbChanges(IALReverb reverb)
		{
			OpenALReverb rv = (reverb as OpenALReverb);
			efx.BindEffectToAuxiliarySlot(
				rv.SlotHandle,
				rv.EffectHandle
			);
#if VERBOSE_AL_DEBUGGING
			CheckALError();
#endif
		}

		public void SetReverbReflectionsDelay(IALReverb reverb, float value)
		{
			efx.Effect(
				(reverb as OpenALReverb).EffectHandle,
				EfxEffectf.EaxReverbReflectionsDelay,
				value / 1000.0f
			);
#if VERBOSE_AL_DEBUGGING
			CheckALError();
#endif
		}

		public void SetReverbDelay(IALReverb reverb, float value)
		{
			efx.Effect(
				(reverb as OpenALReverb).EffectHandle,
                EfxEffectf.ReverbLateReverbDelay,
				value / 1000.0f
			);
#if VERBOSE_AL_DEBUGGING
			CheckALError();
#endif
		}

		public void SetReverbPositionLeft(IALReverb reverb, float value)
		{
			// No known mapping :(
		}

		public void SetReverbPositionRight(IALReverb reverb, float value)
		{
			// No known mapping :(
		}

		public void SetReverbPositionLeftMatrix(IALReverb reverb, float value)
		{
			// No known mapping :(
		}

		public void SetReverbPositionRightMatrix(IALReverb reverb, float value)
		{
			// No known mapping :(
		}

		public void SetReverbEarlyDiffusion(IALReverb reverb, float value)
		{
			// Same as late diffusion, whatever... -flibit
			efx.Effect(
				(reverb as OpenALReverb).EffectHandle,
                EfxEffectf.EaxReverbDiffusion,
				value / 15.0f
			);
#if VERBOSE_AL_DEBUGGING
			CheckALError();
#endif
		}

		public void SetReverbLateDiffusion(IALReverb reverb, float value)
		{
			// Same as early diffusion, whatever... -flibit
			efx.Effect(
				(reverb as OpenALReverb).EffectHandle,
                EfxEffectf.ReverbDiffusion,
				value / 15.0f
			);
#if VERBOSE_AL_DEBUGGING
			CheckALError();
#endif
		}

		public void SetReverbLowEQGain(IALReverb reverb, float value)
		{
			// Cutting off volumes from 0db to 4db! -flibit
			efx.Effect(
				(reverb as OpenALReverb).EffectHandle,
                EfxEffectf.EaxReverbGainLF,
				Math.Min(
					XACTCalculator.CalculateAmplitudeRatio(
						value - 8.0f
					),
					1.0f
				)
			);
#if VERBOSE_AL_DEBUGGING
			CheckALError();
#endif
		}

		public void SetReverbLowEQCutoff(IALReverb reverb, float value)
		{
			efx.Effect(
				(reverb as OpenALReverb).EffectHandle,
                EfxEffectf.EaxReverbLFReference,
				(value * 50.0f) + 50.0f
			);
#if VERBOSE_AL_DEBUGGING
			CheckALError();
#endif
		}

		public void SetReverbHighEQGain(IALReverb reverb, float value)
		{
			efx.Effect(
				(reverb as OpenALReverb).EffectHandle,
                EfxEffectf.EaxReverbGainHF,
				XACTCalculator.CalculateAmplitudeRatio(
					value - 8.0f
				)
			);
#if VERBOSE_AL_DEBUGGING
			CheckALError();
#endif
		}

		public void SetReverbHighEQCutoff(IALReverb reverb, float value)
		{
			efx.Effect(
				(reverb as OpenALReverb).EffectHandle,
                EfxEffectf.EaxReverbHFReference,
				(value * 500.0f) + 1000.0f
			);
#if VERBOSE_AL_DEBUGGING
			CheckALError();
#endif
		}

		public void SetReverbRearDelay(IALReverb reverb, float value)
		{
			// No known mapping :(
		}

		public void SetReverbRoomFilterFrequency(IALReverb reverb, float value)
		{
			// No known mapping :(
		}

		public void SetReverbRoomFilterMain(IALReverb reverb, float value)
		{
			// No known mapping :(
		}

		public void SetReverbRoomFilterHighFrequency(IALReverb reverb, float value)
		{
			// No known mapping :(
		}

		public void SetReverbReflectionsGain(IALReverb reverb, float value)
		{
			// Cutting off possible float values above 3.16, for EFX -flibit
			efx.Effect(
				(reverb as OpenALReverb).EffectHandle,
                EfxEffectf.EaxReverbReflectionsGain,
				Math.Min(
					XACTCalculator.CalculateAmplitudeRatio(value),
					3.16f
				)
			);
#if VERBOSE_AL_DEBUGGING
			CheckALError();
#endif
		}

		public void SetReverbGain(IALReverb reverb, float value)
		{
			// Cutting off volumes from 0db to 20db! -flibit
			efx.Effect(
				(reverb as OpenALReverb).EffectHandle,
                EfxEffectf.ReverbGain,
				Math.Min(
					XACTCalculator.CalculateAmplitudeRatio(value),
					1.0f
				)
			);
#if VERBOSE_AL_DEBUGGING
			CheckALError();
#endif
		}

		public void SetReverbDecayTime(IALReverb reverb, float value)
		{
			/* FIXME: WTF is with this XACT value?
			 * XACT: 0-30 equal to 0.1-inf seconds?!
			 * EFX: 0.1-20 seconds
			 * -flibit
			efx.Effectf(
				(reverb as OpenALReverb).EffectHandle,
				efx._EAXREVERB_GAIN,
				value
			);
			*/
		}

		public void SetReverbDensity(IALReverb reverb, float value)
		{
			efx.Effect(
				(reverb as OpenALReverb).EffectHandle,
                EfxEffectf.EaxReverbDensity,
				value / 100.0f
			);
#if VERBOSE_AL_DEBUGGING
			CheckALError();
#endif
		}

		public void SetReverbRoomSize(IALReverb reverb, float value)
		{
			// No known mapping :(
		}

		public void SetReverbWetDryMix(IALReverb reverb, float value)
		{
			/* FIXME: Note that were dividing by 200, not 100.
			 * For some ridiculous reason the mix is WAY too wet
			 * when we actually do the correct math, but cutting
			 * the ratio in half mysteriously makes it sound right.
			 *
			 * Or, well, "more" right. I'm sure we're still off.
			 * -flibit
			 */
			efx.AuxiliaryEffectSlot(
				(reverb as OpenALReverb).SlotHandle,
				EfxAuxiliaryf.EffectslotGain,
				value / 200.0f
			);
		}

		#endregion

		#region OpenAL Capture Methods

		public IntPtr StartDeviceCapture(string name, int sampleRate, int bufSize)
		{
			var result = Alc.CaptureOpenDevice(
				name,
				(uint) sampleRate,
				ALFormat.Mono16,
				bufSize
			);
			Alc.CaptureStart(result);
#if VERBOSE_AL_DEBUGGING
			if (CheckALCError())
			{
				throw new InvalidOperationException("AL device error!");
			}
#endif
			return result;
		}

		public void StopDeviceCapture(IntPtr handle)
		{
			Alc.CaptureStop(handle);
			Alc.CaptureCloseDevice(handle);
#if VERBOSE_AL_DEBUGGING
			if (CheckALCError())
			{
				throw new InvalidOperationException("AL device error!");
			}
#endif
		}

		public int CaptureSamples(IntPtr handle, IntPtr buffer, int count)
		{
			var samples = new int[1] { 0 };
			Alc.GetInteger(
				handle,
				AlcGetInteger.CaptureSamples,
				1,
				samples
			);
			samples[0] = Math.Min(samples[0], count / 2);
			if (samples[0] > 0)
			{
				Alc.CaptureSamples(handle, buffer, samples[0]);
			}
#if VERBOSE_AL_DEBUGGING
			if (CheckALCError())
			{
				throw new InvalidOperationException("AL device error!");
			}
#endif
			return samples[0] * 2;
		}

		public bool CaptureHasSamples(IntPtr handle)
		{
			var samples = new int[1] { 0 };
			Alc.GetInteger(
				handle,
				AlcGetInteger.CaptureSamples,
				1,
				samples
			);
			return samples[0] > 0;
		}

		#endregion

		#region Private OpenAL Error Check Methods

		private void CheckALError()
		{
			var err = AL.GetError();

			if (err == ALError.NoError)
			{
				return;
			}

			FNALoggerEXT.LogError("OpenAL Error: " + err.ToString("X4"));
#if VERBOSE_AL_DEBUGGING
			throw new InvalidOperationException("OpenAL Error!");
#endif
		}

		#endregion

		#region Private Static XNA->AL Dictionaries

		private static readonly ALFormat[] XNAToShort = new[]
		{
			ALFormat.Mono8,			// NOPE
			ALFormat.Mono16,		// AudioChannels.Mono
			ALFormat.Stereo16,	// AudioChannels.Stereo
		};

		private static readonly ALFormat[] XNAToFloat = new[]
		{
			ALFormat.MonoFloat32Ext,			// NOPE
			ALFormat.MonoFloat32Ext,	// AudioChannels.Mono
			ALFormat.StereoFloat32Ext	// AudioChannels.Stereo
		};

		#endregion

		#region OpenAL Device Enumerators

		public ReadOnlyCollection<RendererDetail> GetDevices()
		{
			var renderers = Alc.GetString(IntPtr.Zero, AlcGetStringList.AllDevicesSpecifier)
				.Select((item, i) => new RendererDetail(
                    item,
                    i.ToString()
                )).ToList();

			return new ReadOnlyCollection<RendererDetail>(renderers);
		}

		public ReadOnlyCollection<Microphone> GetCaptureDevices()
		{
			var microphones = Alc.GetString(IntPtr.Zero, AlcGetStringList.CaptureDeviceSpecifier)
				.Select(item => new Microphone(item))
				.ToList();

			return new ReadOnlyCollection<Microphone>(microphones);
		}

		#endregion
	}
}
