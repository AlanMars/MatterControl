﻿/*
Copyright (c) 2014, Lars Brubaker
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
using MatterControl.Printing;
using MatterHackers.Agg;
using MatterHackers.MatterControl.SlicerConfiguration;
using MatterHackers.VectorMath;

namespace MatterHackers.MatterControl.PrinterCommunication.Io
{
	public class BabyStepsStream : GCodeStreamProxy
	{
		private int extruderIndex = 0;
		private Vector3[] extruderOffsets = new Vector3[4];

		public PrinterMove lastDestination = PrinterMove.Unknown;

		public Vector3[] BabbyStepOffsets { get; private set; } = new Vector3[4];

		public BabyStepsStream(PrinterConfig printer, GCodeStream internalStream)
			: base(printer, internalStream)
		{
			printer.Settings.SettingChanged += Printer_SettingChanged;

			extruderIndex = printer.Connection.ActiveExtruderIndex;

			BabbyStepOffsets[0] = new Vector3(0, 0, printer.Settings.GetValue<double>(SettingsKey.baby_step_z_offset));
			BabbyStepOffsets[1] = new Vector3(0, 0, printer.Settings.GetValue<double>(SettingsKey.baby_step_z_offset_1));

			ReadExtruderOffsets();
		}


		private void ReadExtruderOffsets()
		{
			for (int i = 0; i < 4; i++)
			{
				extruderOffsets[i] = printer.Settings.Helpers.ExtruderOffset(i);
			}
		}

		public override string DebugInfo
		{
			get
			{
				return $"Last Destination = {lastDestination}";
			}
		}

		void Printer_SettingChanged(object s, StringEventArgs e)
		{
			if (e?.Data == SettingsKey.baby_step_z_offset)
			{
				BabbyStepOffsets[0] = new Vector3(0, 0, printer.Settings.GetValue<double>(SettingsKey.baby_step_z_offset));
				printer.Connection.ReadPosition(PositionReadType.Other);
			}
			else if (e?.Data == SettingsKey.baby_step_z_offset_1)
			{
				BabbyStepOffsets[1] = new Vector3(0, 0, printer.Settings.GetValue<double>(SettingsKey.baby_step_z_offset_1));
				printer.Connection.ReadPosition(PositionReadType.Other);
			}
			// if the offsets change update them (unless we are actively printing)
			else if (e?.Data == SettingsKey.extruder_offset
				&& !printer.Connection.Printing
				&& !printer.Connection.Paused)
			{
				ReadExtruderOffsets();
				printer.Connection.ReadPosition(PositionReadType.Other);
			}
		}

		public override void SetPrinterPosition(PrinterMove position)
		{
			this.lastDestination.CopyKnowSettings(position);
			if (extruderIndex < 4)
			{
				lastDestination.position -= BabbyStepOffsets[extruderIndex];
				lastDestination.position += extruderOffsets[extruderIndex];
			}
			internalStream.SetPrinterPosition(lastDestination);
		}

		public override void Dispose()
		{
			printer.Settings.SettingChanged -= Printer_SettingChanged;

			base.Dispose();
		}

		public override string ReadLine()
		{
			string lineToSend = base.ReadLine();

			if (lineToSend != null
				&& lineToSend.EndsWith("; NO_PROCESSING"))
			{
				return lineToSend;
			}

			if (lineToSend != null
				&& lineToSend.StartsWith("T"))
			{
				int extruder = 0;
				if (GCodeFile.GetFirstNumberAfter("T", lineToSend, ref extruder))
				{
					extruderIndex = extruder;
				}
			}

			if (lineToSend != null
				&& LineIsMovement(lineToSend))
			{
				PrinterMove currentMove = GetPosition(lineToSend, lastDestination);

				PrinterMove moveToSend = currentMove;
				if (extruderIndex < 4)
				{
					moveToSend.position += BabbyStepOffsets[extruderIndex];
					moveToSend.position -= extruderOffsets[extruderIndex];
				}

				if (moveToSend.HaveAnyPosition)
				{
					lineToSend = CreateMovementLine(moveToSend, lastDestination);
				}
				lastDestination = currentMove;

				return lineToSend;
			}

			return lineToSend;
		}
	}
}