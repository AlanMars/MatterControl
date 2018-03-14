﻿/*
Copyright (c) 2018, Lars Brubaker, John Lewin
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

using System.ComponentModel;
using System.IO;
using MatterHackers.Agg;
using MatterHackers.Agg.Font;
using MatterHackers.Agg.Platform;
using MatterHackers.Agg.Transform;
using MatterHackers.Agg.UI;
using MatterHackers.Agg.VertexSource;
using MatterHackers.DataConverters3D;
using MatterHackers.MatterControl.DesignTools.Operations;
using MatterHackers.MatterControl.Plugins.BrailleBuilder;
using MatterHackers.VectorMath;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace MatterHackers.MatterControl.DesignTools
{
	[WebPageLink("About Braille", "https://en.wikipedia.org/wiki/Braille")]
	public class BrailleCardObject3D : Object3D, IRebuildable
	{
		public BrailleCardObject3D()
		{
		}

		public static BrailleCardObject3D Create()
		{
			var item = new BrailleCardObject3D();

			item.Rebuild(null);
			return item;
		}

		public override string ActiveEditor => "PublicPropertyEditor";

		public char Letter { get; set; } = 'a';

		public double BaseHeight { get; set; } = 4;

		public void Rebuild(UndoBuffer undoBuffer)
		{
			var aabb = this.GetAxisAlignedBoundingBox();

			this.Children.Modify(list =>
			{
				list.Clear();
			});

			var brailleLetter = new BrailleObject3D()
			{
				TextToEncode = Letter.ToString(),
				BaseHeight = BaseHeight,
			};
			brailleLetter.Rebuild(null);
			this.Children.Add(brailleLetter);

			var textObject = new TextObject3D()
			{
				PointSize = 46,
				Color = Color.LightBlue,
				NameToWrite = Letter.ToString(),
				Height = BaseHeight
			};
			textObject.Rebuild(null);
			IObject3D letterObject = new Rotate(textObject, -MathHelper.Tau / 4);
			letterObject = new Align(letterObject, Face.Bottom | Face.Front, brailleLetter, Face.Top | Face.Front, 0, 0, 3.5);
			letterObject = new SetCenter(letterObject, brailleLetter.GetCenter(), true, false, false);
			this.Children.Add(letterObject);

			var basePath = new RoundedRect(0, 0, 22, 34, 3)
			{
				ResolutionScale = 10
			};

			IObject3D basePlate = new Object3D()
			{
				Mesh = VertexSourceToMesh.Extrude(basePath, BaseHeight),
				Matrix = Matrix4X4.CreateRotationX(MathHelper.Tau / 4)
			};

			basePlate = new Align(basePlate, Face.Bottom | Face.Back, brailleLetter, Face.Bottom | Face.Back);
			basePlate = new SetCenter(basePlate, brailleLetter.GetCenter(), true, false, false);
			this.Children.Add(basePlate);

			IObject3D underline = new CubeObject3D(basePlate.XSize(), .2, 1);
			underline = new Align(underline, Face.Bottom, brailleLetter, Face.Top);
			underline = new Align(underline, Face.Back | Face.Left, basePlate, Face.Front | Face.Left, 0, .01);
			this.Children.Add(underline);

			if (aabb.ZSize > 0)
			{
				// If the part was already created and at a height, maintain the height.
				PlatingHelper.PlaceMeshAtHeight(this, aabb.minXYZ.Z);
			}
		}
	}
}