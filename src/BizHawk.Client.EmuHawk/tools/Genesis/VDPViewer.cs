﻿using System;
using System.Drawing;
using System.Windows.Forms;
using System.Drawing.Imaging;

using BizHawk.Client.Common;
using BizHawk.Emulation.Common;
using BizHawk.Emulation.Cores.Consoles.Sega.gpgx;
using BizHawk.Common;

namespace BizHawk.Client.EmuHawk
{
	public partial class GenVdpViewer : ToolFormBase, IToolFormAutoConfig
	{
		[RequiredService]
		private GPGX Emu { get; set; }

		private GPGX.VDPView _view;

		private int _palIndex;

		protected override Point ScrollToControl(Control activeControl)
		{
			// Returning the current location prevents the panel from scrolling to the active control when the panel loses and regains focus
			return DisplayRectangle.Location;
		}

		public GenVdpViewer()
		{
			InitializeComponent();
			bmpViewTiles.ChangeBitmapSize(512, 256);
			bmpViewPal.ChangeBitmapSize(16, 4);
		}

		private static unsafe void DrawTile(int* dest, int pitch, byte* src, int* pal)
		{
			for (int j = 0; j < 8; j++)
			{
				*dest++ = pal[*src++];
				*dest++ = pal[*src++];
				*dest++ = pal[*src++];
				*dest++ = pal[*src++];
				*dest++ = pal[*src++];
				*dest++ = pal[*src++];
				*dest++ = pal[*src++];
				*dest++ = pal[*src++];
				dest += pitch - 8;
			}
		}

		private static unsafe void DrawNameTable(LibGPGX.VDPNameTable nt, ushort* vram, byte* tiles, int* pal, BmpView bv)
		{
			ushort* nametable = vram + nt.Baseaddr / 2;
			int tileW = nt.Width;
			int tileH = nt.Height;

			Size pixSize = new Size(tileW * 8, tileH * 8);
			bv.Size = pixSize;
			bv.ChangeBitmapSize(pixSize);

			var lockData = bv.BMP.LockBits(new Rectangle(Point.Empty, pixSize), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
			int pitch = lockData.Stride / sizeof(int);
			int* dest = (int*)lockData.Scan0;

			for (int tileY = 0; tileY < tileH; tileY++)
			{
				for (int tileX = 0; tileX < tileW; tileX++)
				{
					ushort bgent = *nametable++;
					int palidx = bgent >> 9 & 0x30;
					int tileent = bgent & 0x1fff; // h and v flip are stored separately in cache
					DrawTile(dest, pitch, tiles + tileent * 64, pal + palidx);
					dest += 8;
				}
				dest -= 8 * tileW;
				dest += 8 * pitch;
			}
			bv.BMP.UnlockBits(lockData);
			bv.Refresh();
		}

		unsafe void DrawPalettes(int* pal)
		{
			var lockData = bmpViewPal.BMP.LockBits(new Rectangle(0, 0, 16, 4), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
			int pitch = lockData.Stride / sizeof(int);
			int* dest = (int*)lockData.Scan0;

			for (int j = 0; j < 4; j++)
			{
				for (int i = 0; i < 16; i++)
					*dest++ = *pal++;
				dest += pitch - 16;
			}
			bmpViewPal.BMP.UnlockBits(lockData);
			bmpViewPal.Refresh();
		}

		unsafe void DrawTiles()
		{
			var lockData = bmpViewTiles.BMP.LockBits(new Rectangle(0, 0, 512, 256), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
			int pitch = lockData.Stride / sizeof(int);
			int* dest = (int*)lockData.Scan0;
			byte* src = (byte*)_view.PatternCache;

			int* pal = 0x10 * _palIndex + (int*)_view.ColorCache;

			for (int tile = 0; tile < 2048;)
			{
				DrawTile(dest, pitch, src, pal);
				dest += 8;
				src += 64;
				tile++;
				if ((tile & 63) == 0)
					dest += 8 * pitch - 512;
			}
			bmpViewTiles.BMP.UnlockBits(lockData);
			bmpViewTiles.Refresh();
		}

		public void NewUpdate(ToolFormUpdateType type) { }

		public unsafe void UpdateValues()
		{
			if (Emu == null)
			{
				return;
			}

			using ((_view = Emu.UpdateVDPViewContext()).EnterExit())
			{
				int* pal = (int*)_view.ColorCache;
				DrawPalettes(pal);
				DrawTiles();
				ushort* vramNt = (ushort*)_view.VRAM;
				byte* tiles = (byte*)_view.PatternCache;
				DrawNameTable(_view.NTA, vramNt, tiles, pal, bmpViewNTA);
				DrawNameTable(_view.NTB, vramNt, tiles, pal, bmpViewNTB);
				DrawNameTable(_view.NTW, vramNt, tiles, pal, bmpViewNTW);
				_view = null;
			}
		}

		public void FastUpdate()
		{
			// Do nothing
		}

		public void Restart()
		{
			UpdateValues();
		}

		public bool AskSaveChanges() => true;

		public bool UpdateBefore => true;

		private void bmpViewPal_MouseClick(object sender, MouseEventArgs e)
		{
			int idx = e.Y / 16;
			idx = Math.Min(3, Math.Max(idx, 0));
			_palIndex = idx;
			UpdateValues();
		}

		private void VDPViewer_KeyDown(object sender, KeyEventArgs e)
		{
			if (ModifierKeys.HasFlag(Keys.Control) && e.KeyCode == Keys.C)
			{
				// find the control under the mouse
				Point m = Cursor.Position;
				Control top = this;
				Control found;
				do
				{
					found = top.GetChildAtPoint(top.PointToClient(m));
					top = found;
				} while (found != null && found.HasChildren);

				if (found is BmpView bv)
				{
					Clipboard.SetImage(bv.BMP);
				}
			}
		}

		private void SaveBGAScreenshotToolStripMenuItem_Click(object sender, EventArgs e)
		{
			bmpViewNTA.SaveFile();
		}

		private void SaveBGBScreenshotToolStripMenuItem_Click(object sender, EventArgs e)
		{
			bmpViewNTB.SaveFile();
		}

		private void SaveTilesScreenshotToolStripMenuItem_Click(object sender, EventArgs e)
		{
			bmpViewTiles.SaveFile();
		}

		private void SaveWindowScreenshotToolStripMenuItem_Click(object sender, EventArgs e)
		{
			bmpViewNTW.SaveFile();
		}

		private void SavePaletteScreenshotToolStripMenuItem_Click(object sender, EventArgs e)
		{
			bmpViewPal.SaveFile();
		}

		private void CloseMenuItem_Click(object sender, EventArgs e)
		{
			Close();
		}
	}
}
