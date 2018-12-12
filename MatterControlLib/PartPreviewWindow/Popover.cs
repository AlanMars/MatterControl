﻿/*
Copyright (c) 2017, Lars Brubaker, John Lewin
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.
2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

The views and conclusions contained in the software and documentation are those
of the authors and should not be interpreted as representing official policies,
either expressed or implied, of the FreeBSD Project.
*/

using System;
using MatterHackers.Agg;
using MatterHackers.Agg.UI;
using MatterHackers.Agg.VertexSource;

namespace MatterHackers.MatterControl.PartPreviewWindow
{
	public class Popover : GuiWidget
	{
		private IVertexSource tabShape = null;

		public Color TagColor { get; set; }

		public ArrowDirection Arrow { get; set; }

		public override void OnBoundsChanged(EventArgs e)
		{
			base.OnBoundsChanged(e);

			Console.WriteLine("Height: " + this.Height);

			// Works for left right
			//tabShape = GetShape(this.Arrow, this.LocalBounds, this.NotchSize, this.LocalBounds.Top - this.P2);

			// Troubleshooting top/bottom
			tabShape = GetShape(this.Arrow, this.LocalBounds, this.NotchSize, this.LocalBounds.Left + 20);

		}

		public int P2 { get; set; }

		public int NotchSize { get; set; } = 5;

		private static IVertexSource GetShape(ArrowDirection arrowDirection, RectangleDouble rect, double notchSize, double p2)
		{
			var tabShape = new VertexStorage();
			var centerY = rect.YCenter;

			// Tab - core
			var radius = 4.0;

			double x0 = rect.Left;
			double x1 = x0 + (arrowDirection == ArrowDirection.Left ? notchSize : 0);
			double x2 = x1 + radius;

			double x5 = rect.Right;
			double x4 = x5 - (arrowDirection == ArrowDirection.Right ? notchSize : 0);
			double x3 = x4 - radius;

			double y0 = rect.Top;
			double y1 = y0 - (arrowDirection == ArrowDirection.Top ? notchSize : 0);
			double y2 = y1 - radius;

			double y5 = rect.Bottom;
			double y4 = y5 + (arrowDirection == ArrowDirection.Bottom ? notchSize : 0);
			double y3 = y4 + radius;

			int p1, p3;

			if (arrowDirection == ArrowDirection.Bottom
				|| arrowDirection == ArrowDirection.Right)
			{
				p1 = (int)(p2 + notchSize);
				p3 = (int)(p2 - notchSize);
			}
			else
			{
				p1 = (int)(p2 - notchSize);
				p3 = (int)(p2 + notchSize);
			}

			int notchX = (int)(p2 - notchSize);

			tabShape.MoveTo(x2, y4); // A
			tabShape.curve3(x1, y4, x1, y3); // A -> B

			if (arrowDirection != ArrowDirection.Left)
			{
				// B -> C
				tabShape.LineTo(x1, y2);
			}
			else
			{
				// Left Notch (B -> C through P1:P4)
				tabShape.LineTo(x1, p1); // B -> P1

				// Notch
				tabShape.LineTo(x0, p2); // P1 -> P2
				tabShape.LineTo(x1, p3); // P2 -> P3
				tabShape.LineTo(x1, y2); // P3 -> P4
			}

			tabShape.curve3(x1, y1, x2, y1); // C -> D

			if (arrowDirection != ArrowDirection.Top)
			{
				// D -> E
				tabShape.LineTo(x3, y1);
			}
			else
			{
				// D -> E through (P1:P3)
				tabShape.LineTo(p1, y1); // F -> P1

				// Notch
				tabShape.LineTo(p2, y0); // P1 -> P2
				tabShape.LineTo(p3, y1); // P2 -> P3
				tabShape.LineTo(x3, y1); // P3 -> E
			}

			tabShape.curve3(x4, y1, x4, y2); // E -> F

			if (arrowDirection != ArrowDirection.Right)
			{
				// F -> G
				tabShape.LineTo(x4, y3);
			}
			else
			{

				Console.WriteLine("gah: {0}/{1}/{2}", p1, p2, p3);

				// F -> G through P1-P3
				tabShape.LineTo(x4, p1); // F -> P1

				// Notch
				tabShape.LineTo(x5, p2); // P1 -> P2
				tabShape.LineTo(x4, p3); // P2 -> P3
				tabShape.LineTo(x4, y3); // P3 -> G
			}

			tabShape.curve3(x4, y4, x3, y4); // G -> H

			if (arrowDirection != ArrowDirection.Bottom)
			{
				// H -> A
				tabShape.LineTo(x2, y4);
			}
			else
			{
				// H -> A (through P1:P3)
				tabShape.LineTo(notchX + (notchSize * 2), y4); // F -> P1

				// Notch
				tabShape.LineTo(p2, y5); // P1 -> P2
				tabShape.LineTo(notchX, y4); // P2 -> P3
				tabShape.LineTo(x3, y4); // P3 -> A
			}

			return new FlattenCurves(tabShape);
		}

		public enum ArrowDirection { Right, Left, Top, Bottom }

		public override void OnDrawBackground(Graphics2D graphics2D)
		{
			base.OnDrawBackground(graphics2D);

			if (tabShape != null)
			{
				graphics2D.Render(tabShape, this.TagColor);
			}
		}
	}
}
