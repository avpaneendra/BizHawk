﻿using BizHawk.Common.NumberExtensions;
using System;
using BizHawk.Common;
using BizHawk.Emulation.Common;

namespace BizHawk.Emulation.Cores.Intellivision
{
	public sealed class PSG : IAsyncSoundProvider
	{
		public ushort[] Register = new ushort[16];

		public void Reset()
		{
			sq_per_A = sq_per_B = sq_per_C = 0x1000;
			noise_per = 64;
			env_per = 0x20000;
		}

		public void DiscardSamples()
		{
			
			sample_count = 0;

			for (int i = 0; i < 3733; i++)
			{
				audio_samples[i] = 0;
			}
		}

		public void GetSamples(short[] samples)
		{
			for (int i = 0; i < samples.Length / 2; i++)
			{
				//smooth out audio sample by averging
				samples[i * 2] = (short)(audio_samples[(int)Math.Floor(3.7904 * i)]);
				
				samples[(i * 2) + 1] = samples[i * 2];
			}


		}

		// There is one audio clock for every 4 cpu clocks, and ~15000 cycles per frame
		public short[] audio_samples = new short[4000];

		public static int[] volume_table = new int[16] {0x0000, 0x0055, 0x0079, 0x00AB, 0x00F1, 0x0155, 0x01E3, 0x02AA,
0x03C5, 0x0555, 0x078B, 0x0AAB, 0x0F16, 0x1555, 0x1E2B, 0x2AAA};

		public int sample_count;

		public int TotalExecutedCycles;
		public int PendingCycles;

		public int psg_clock;

		public int sq_per_A, sq_per_B, sq_per_C;
		public int clock_A, clock_B, clock_C;
		public int vol_A, vol_B, vol_C;
		public bool A_on, B_on, C_on;
		public bool A_up, B_up, C_up;
		public bool A_noise, B_noise, C_noise;

		public int env_per;
		public int env_clock;
		public int env_shape;
		public int env_vol_A, env_vol_B, env_vol_C;

		public int noise_clock;
		public int noise_per;
		public int noise=0x1FFF;

		public Func<ushort, ushort> ReadMemory;
		public Func<ushort, ushort, bool> WriteMemory;

		public void SyncState(Serializer ser)
		{
			ser.BeginSection("PSG");

			ser.Sync("Register", ref Register, false);
			ser.Sync("Toal_executed_cycles", ref TotalExecutedCycles);
			ser.Sync("Pending Cycles", ref PendingCycles);

			ser.Sync("sample_count", ref sample_count);
			ser.Sync("psg_clock", ref psg_clock);
			ser.Sync("clock_A", ref clock_A);
			ser.Sync("clock_B", ref clock_B);
			ser.Sync("clock_C", ref clock_C);
			ser.Sync("noise clock", ref noise_clock);
			ser.Sync("A_up", ref A_up);
			ser.Sync("B_up", ref B_up);
			ser.Sync("C_up", ref C_up);
			ser.Sync("noise", ref noise);

			sync_psg_state();

			ser.EndSection();
		}

		public ushort? ReadPSG(ushort addr)
		{
			if (addr >= 0x01F0 && addr <= 0x01FF)
			{
				return (ushort)(0xFF00 | Register[addr - 0x01F0]);
			}
			return null;
		}

		public void sync_psg_state()
		{

			sq_per_A = (Register[0] & 0xFF) | (((Register[4] & 0xF) << 8));
			if (sq_per_A == 0)
				sq_per_A = 0x1000;
			//else
				//sq_per_A *= 2;
			//clock_A = 0;

			sq_per_B = (Register[1] & 0xFF) | (((Register[5] & 0xF) << 8));
			if (sq_per_B == 0)
				sq_per_B = 0x1000;
			//else
				//sq_per_B *= 2;
			//clock_B = 0;

			sq_per_C = (Register[2] & 0xFF) | (((Register[6] & 0xF) << 8));
			if (sq_per_C == 0)
				sq_per_C = 0x1000;
			//else
				//sq_per_C *= 2;
			//clock_C = 0;

			env_per = (Register[3] & 0xFF) | (((Register[7] & 0xFF) << 8));
			if (env_per == 0)
				env_per = 0x20000;
			else
				env_per *= 2;

			A_on = Register[8].Bit(0);
			B_on = Register[8].Bit(1);
			C_on = Register[8].Bit(2);
			A_noise = Register[8].Bit(3);
			B_noise = Register[8].Bit(4);
			C_noise = Register[8].Bit(5);

			noise_per = Register[9] & 0x1F;
			if (noise_per == 0)
			{
				noise_per = 64;
			}
			else
			{
				noise_per *= 2;
			}

			var shape_select = Register[10] & 0xF;

			if (shape_select < 4)
				env_shape = 0;
			else if (shape_select < 8)
				env_shape = 1;
			else
				env_shape = 2 + (shape_select - 8);

			vol_A = Register[11] & 0xF;
			env_vol_A = (Register[11] >> 4) & 0x3;

			vol_B = Register[12] & 0xF;
			env_vol_B = (Register[12] >> 4) & 0x3;


			vol_C = Register[13] & 0xF;
			env_vol_C = (Register[13] >> 4) & 0x3;

		}

		public bool WritePSG(ushort addr, ushort value)
		{
			if (addr >= 0x01F0 && addr <= 0x01FF)
			{
				Register[addr - 0x01F0] = value;
				if (addr - 0x01F0 == 10)
					env_clock = 0;

				sync_psg_state();

				return true;
			}
			return false;
		}

		public void generate_sound(int cycles_to_do)
		{
			// there are 4 cpu cycles for every psg cycle
			bool sound_out_A;
			bool sound_out_B;
			bool sound_out_C;

			for (int i=0;i<cycles_to_do;i++)
			{
				psg_clock++;

				if (psg_clock==4)
				{
					psg_clock = 0;

					if (vol_A!=0)
						clock_A++;
					else if (env_vol_A!=0)
						clock_A++;

					if (vol_B != 0)
						clock_B++;
					else if (env_vol_B != 0)
						clock_B++;

					if (vol_C != 0)
						clock_C++;
					else if (env_vol_C != 0)
						clock_C++;

					env_clock++;
					noise_clock++;

					//clock noise
					if (noise_clock >= noise_per)
					{
						noise = (noise >> 1) ^ (noise.Bit(0) ? 0x10004 : 0);
						noise_clock = 0;
					}

					if (clock_A >= sq_per_A)
					{
						A_up = !A_up;
						clock_A = 0;
					}

					if (clock_B >= sq_per_B)
					{
						B_up = !B_up;
						clock_B = 0;
					}

					if (clock_C >= sq_per_C)
					{
						C_up = !C_up;
						clock_C = 0;
					}


					sound_out_A = (noise.Bit(0) | A_noise) & (A_on | A_up);
					sound_out_B = (noise.Bit(0) | B_noise) & (B_on | B_up);
					sound_out_C = (noise.Bit(0) | C_noise) & (C_on | C_up);

					//now calculate the volume of each channel and add them together

					if (env_vol_A == 0)
					{
						audio_samples[sample_count] = (short)(sound_out_A ? volume_table[vol_A] : 0);
					}
					else
					{
						//Console.Write(env_vol_A); Console.Write("A"); Console.Write('\n');
					}

					if (env_vol_B == 0)
					{
						audio_samples[sample_count] += (short)(sound_out_B ? volume_table[vol_B] : 0);
					}
					else
					{
						//Console.Write(env_vol_B); Console.Write("B"); Console.Write('\n');
					}

					if (env_vol_C == 0)
					{
						audio_samples[sample_count] += (short)(sound_out_C ? volume_table[vol_C] : 0);
					}
					else
					{
						//Console.Write(env_vol_C); Console.Write("C"); Console.Write('\n');
					}

					sample_count++;

				}

				

			}

		}
	}
}
