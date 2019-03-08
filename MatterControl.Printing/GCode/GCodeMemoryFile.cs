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
#define DUMP_SLOW_TIMES

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using MatterHackers.Agg;
using MatterHackers.VectorMath;

namespace MatterControl.Printing
{
	public class GCodeMemoryFile : GCodeFile
	{
		private double diameterOfFilamentUsedMmCache = 0;
		private double filamentDiameterCache = 0;
		private double filamentUsedMmCache = 0;
		private bool foundFirstLayerMarker;
		private List<PrinterMachineInstruction> GCodeCommandQueue = new List<PrinterMachineInstruction>();
		private bool gcodeHasExplicitLayerChangeInfo = false;
		private int lastPrintLine;
		private List<double> layerHeights = new List<double>();
		private double parsingLastZ;
		private List<int> toolChanges = new List<int>();

		public GCodeMemoryFile(bool gcodeHasExplicitLayerChangeInfo = false)
		{
			this.gcodeHasExplicitLayerChangeInfo = gcodeHasExplicitLayerChangeInfo;
		}

		public List<int> IndexOfLayerStart { get; private set; } = new List<int>();

		public override int LayerCount
		{
			get { return IndexOfLayerStart.Count; }
		}

		public override int LineCount => GCodeCommandQueue.Count;

		public HashSet<float> Speeds { get; private set; }

		public override double TotalSecondsInPrint => Instruction(0).SecondsToEndFromHere;

		public static GCodeMemoryFile Load(Stream fileStream,
			Vector4 maxAccelerationMmPerS2,
			Vector4 maxVelocityMmPerS,
			Vector4 velocitySameAsStopMmPerS,
			Vector4 speedMultiplier,
			CancellationToken cancellationToken,
			Action<double, string> progressReporter = null)
		{
			try
			{
				using (var reader = new StreamReader(fileStream))
				{
					var gcodeMemoryFile = ParseFileContents(reader.ReadToEnd(),
						maxAccelerationMmPerS2, maxVelocityMmPerS, velocitySameAsStopMmPerS, speedMultiplier,
						cancellationToken, progressReporter);

					return gcodeMemoryFile;
				}
			}
			catch (Exception e)
			{
				Debug.Print(e.Message);
			}

			return null;
		}

		public static GCodeMemoryFile Load(string filePath,
			Vector4 maxAccelerationMmPerS2,
			Vector4 maxVelocityMmPerS,
			Vector4 velocitySameAsStopMmPerS,
			Vector4 speedMultiplier,
			CancellationToken cancellationToken, Action<double, string> progressReporter)
		{
			if (Path.GetExtension(filePath).ToUpper() == ".GCODE")
			{
				try
				{
					using (var stream = File.OpenRead(filePath))
					{
						return Load(stream,
							maxAccelerationMmPerS2,
							maxVelocityMmPerS,
							velocitySameAsStopMmPerS,
							speedMultiplier,
							cancellationToken, progressReporter);
					}
				}
				catch (Exception e)
				{
					Debug.Print(e.Message);
				}
			}

			return null;
		}

		public void Add(PrinterMachineInstruction printerMachineInstruction)
		{
			Insert(LineCount, printerMachineInstruction);
		}

		public override void Clear()
		{
			IndexOfLayerStart.Clear();
			GCodeCommandQueue.Clear();
		}

		public override RectangleDouble GetBounds()
		{
			RectangleDouble bounds = new RectangleDouble(double.MaxValue, double.MaxValue, double.MinValue, double.MinValue);
			foreach (PrinterMachineInstruction state in GCodeCommandQueue)
			{
				bounds.Left = Math.Min(state.Position.X, bounds.Left);
				bounds.Right = Math.Max(state.Position.X, bounds.Right);
				bounds.Bottom = Math.Min(state.Position.Y, bounds.Bottom);
				bounds.Top = Math.Max(state.Position.Y, bounds.Top);
			}

			return bounds;
		}

		public override double GetFilamentCubicMm(double filamentDiameterMm)
		{
			double filamentUsedMm = GetFilamentUsedMm(filamentDiameterMm);
			double filamentRadius = filamentDiameterMm / 2;
			double areaSquareMm = (filamentRadius * filamentRadius) * Math.PI;

			return areaSquareMm * filamentUsedMm;
		}

		public override double GetFilamentDiameter()
		{
			if (filamentDiameterCache == 0)
			{
				// check the beginning of the file for the filament diameter
				for (int i = 0; i < Math.Min(100, GCodeCommandQueue.Count); i++)
				{
					if (FindDiameter(i, ref filamentDiameterCache))
					{
						break;
					}
				}

				// check the end of the file for the filament diameter
				if (filamentDiameterCache == 0)
				{
					// didn't find it, so look at the end of the file for filament_diameter =
					for (int i = GCodeCommandQueue.Count - 1; i > Math.Max(0, GCodeCommandQueue.Count - 100); i--)
					{
						if (FindDiameter(i, ref filamentDiameterCache))
						{
							break;
						}
					}
				}

				if (filamentDiameterCache == 0)
				{
					// it is still 0 so set it to something so we render
					filamentDiameterCache = 1.75;
				}
			}

			return filamentDiameterCache;
		}

		public override double GetFilamentUsedMm(double filamentDiameter)
		{
			if (filamentUsedMmCache == 0 || filamentDiameter != diameterOfFilamentUsedMmCache)
			{
				double lastEPosition = 0;
				double filamentMm = 0;
				for (int i = 0; i < GCodeCommandQueue.Count; i++)
				{
					PrinterMachineInstruction instruction = GCodeCommandQueue[i];
					//filamentMm += instruction.EPosition;

					string lineToParse = instruction.Line;
					if (lineToParse.StartsWith("G0") || lineToParse.StartsWith("G1"))
					{
						double ePosition = lastEPosition;
						if (GetFirstNumberAfter("E", lineToParse, ref ePosition))
						{
							if (instruction.MovementType == PrinterMachineInstruction.MovementTypes.Absolute)
							{
								double deltaEPosition = ePosition - lastEPosition;
								filamentMm += deltaEPosition;
							}
							else
							{
								filamentMm += ePosition;
							}

							lastEPosition = ePosition;
						}
					}
					else if (lineToParse.StartsWith("G92"))
					{
						double ePosition = 0;
						if (GetFirstNumberAfter("E", lineToParse, ref ePosition))
						{
							lastEPosition = ePosition;
						}
					}
				}

				filamentUsedMmCache = filamentMm;
				diameterOfFilamentUsedMmCache = filamentDiameter;
			}

			return filamentUsedMmCache;
		}

		public override double GetFilamentWeightGrams(double filamentDiameterMm, double densityGramsPerCubicCm)
		{
			double cubicMmPerCubicCm = 1000;
			double gramsPerCubicMm = densityGramsPerCubicCm / cubicMmPerCubicCm;
			double cubicMms = GetFilamentCubicMm(filamentDiameterMm);
			return cubicMms * gramsPerCubicMm;
		}

		public override int GetFirstLayerInstruction(int layerIndex)
		{
			if (layerIndex < IndexOfLayerStart.Count)
			{
				return IndexOfLayerStart[layerIndex];
			}

			// else return the last instruction
			return GCodeCommandQueue.Count - 1;
		}

		/// <summary>
		/// Get the height of the bottom of this layer as measure from the bed
		/// </summary>
		/// <param name="layerIndex"></param>
		/// <returns></returns>
		public double GetLayerBottom(int layerIndex)
		{
			double total = 0;
			for (int i = 0; i < layerIndex; i++)
			{
				total += GetLayerHeight(i);
			}
			return total;
		}

		/// <summary>
		/// Get the height of this layer (from the top of the previous layer to the top of this layer).
		/// </summary>
		/// <param name="layerIndex"></param>
		/// <returns></returns>
		public override double GetLayerHeight(int layerIndex)
		{
			if (layerHeights.Count > 0)
			{
				if (layerIndex < layerHeights.Count)
				{
					return layerHeights[layerIndex];
				}

				return 0;
			}

			if (IndexOfLayerStart.Count > 2)
			{
				return GCodeCommandQueue[IndexOfLayerStart[2]].Z - GCodeCommandQueue[IndexOfLayerStart[1]].Z;
			}

			return .5;
		}

		public override int GetLayerIndex(int instructionIndex)
		{
			if (instructionIndex >= 0
				&& instructionIndex <= LineCount)
			{
				for (var i = IndexOfLayerStart.Count - 1; i >= 0; i--)
				{
					var lineStart = IndexOfLayerStart[i];

					if (instructionIndex >= lineStart)
					{
						return i;
					}
				}
			}

			return -1;
		}

		/// <summary>
		/// Get the height of the top of this layer as measure from the bed
		/// </summary>
		/// <param name="layerIndex"></param>
		/// <returns></returns>
		public override double GetLayerTop(int layerIndex)
		{
			double total = 0;
			for (int i = 0; i <= layerIndex; i++)
			{
				total += GetLayerHeight(i);
			}
			return total;
		}

		public override Vector2 GetWeightedCenter()
		{
			MatterHackers.VectorMath.Vector2 total = new MatterHackers.VectorMath.Vector2();
			foreach (PrinterMachineInstruction state in GCodeCommandQueue)
			{
				total += new MatterHackers.VectorMath.Vector2(state.Position.X, state.Position.Y);
			}

			return total / GCodeCommandQueue.Count;
		}

		public void Insert(int insertIndex, PrinterMachineInstruction printerMachineInstruction)
		{
			for (int i = 0; i < IndexOfLayerStart.Count; i++)
			{
				if (insertIndex < IndexOfLayerStart[i])
				{
					IndexOfLayerStart[i]++;
				}
			}

			GCodeCommandQueue.Insert(insertIndex, printerMachineInstruction);
		}

		public override PrinterMachineInstruction Instruction(int index)
		{
			return GCodeCommandQueue[index];
		}

		public override bool IsExtruding(int instructionIndexToCheck)
		{
			if (instructionIndexToCheck > 1 && instructionIndexToCheck < GCodeCommandQueue.Count)
			{
				double extrusionLength = GCodeCommandQueue[instructionIndexToCheck].EPosition - GCodeCommandQueue[instructionIndexToCheck - 1].EPosition;
				if (extrusionLength > 0)
				{
					return true;
				}
			}

			return false;
		}

		public (int toolIndex, double time) NextToolChange(int instructionIndex, int currentToolIndex = -1, int toolToLookFor = -1)
		{
			if (GCodeCommandQueue.Count > 0)
			{
				int nextToolChange = -1;
				// find the first tool change that we are less than
				for (int i = 0; i < toolChanges.Count; i++)
				{
					if (instructionIndex < toolChanges[i]
						&& GCodeCommandQueue[toolChanges[i]].ToolIndex != currentToolIndex
						&& (toolToLookFor == -1 || GCodeCommandQueue[toolChanges[i]].ToolIndex == toolToLookFor))
					{
						nextToolChange = i;
						break;
					}
				}

				if (nextToolChange >= 0)
				{
					var toolIndex = GCodeCommandQueue[toolChanges[nextToolChange]].ToolIndex;
					var time = GCodeCommandQueue[instructionIndex].SecondsToEndFromHere - GCodeCommandQueue[toolChanges[nextToolChange]].SecondsToEndFromHere;
					return (toolIndex, time);
				}
				else
				{
					// don't turn of extruders if we will end the print within 10 minutes
					if (instructionIndex < GCodeCommandQueue.Count
						&& GCodeCommandQueue[instructionIndex].SecondsToEndFromHere < 60 * 10)
					{
						return (toolToLookFor, 60 * 10);
					}
				}
			}

			// there are no more tool changes
			return (currentToolIndex, double.PositiveInfinity);
		}

		public override double PercentComplete(int instructionIndex)
		{
			if (GCodeCommandQueue.Count > 0)
			{
				return Math.Min(99.9, (double)instructionIndex / (double)GCodeCommandQueue.Count * 100);
			}

			return 100;
		}

		public override double Ratio0to1IntoContainedLayer(int instructionIndex)
		{
			int currentLayer = GetLayerIndex(instructionIndex);

			if (currentLayer > -1)
			{
				int startIndex = IndexOfLayerStart[currentLayer];

				int endIndex = LineCount - 1;

				if (currentLayer < LayerCount - 1)
				{
					endIndex = IndexOfLayerStart[currentLayer + 1];
				}
				else
				{
					// Improved last layer percent complete - seek endIndex to 'MatterSlice Completed' line, otherwise leave at LineCount - 1
					if (lastPrintLine == -1)
					{
						string line = "";
						lastPrintLine = instructionIndex;
						do
						{
							line = GCodeCommandQueue[Math.Min(GCodeCommandQueue.Count - 1, lastPrintLine)].Line;
							lastPrintLine++;
						} while (line != "; MatterSlice Completed Successfully"
							&& lastPrintLine < endIndex);
					}

					endIndex = lastPrintLine;
				}

				int deltaFromStart = Math.Max(0, instructionIndex - startIndex);
				return deltaFromStart / (double)(endIndex - startIndex);
			}

			return 0;
		}

		public void Save(string dest)
		{
			using (StreamWriter file = new StreamWriter(dest))
			{
				foreach (PrinterMachineInstruction instruction in GCodeCommandQueue)
				{
					file.WriteLine(instruction.Line);
				}
			}
		}

		private static int CountNumLines(string gCodeString)
		{
			int crCount = 0;
			foreach (char testCharacter in gCodeString)
			{
				if (testCharacter == '\n')
				{
					crCount++;
				}
			}

			return crCount + 1;
		}

		private static IEnumerable<string> CustomSplit(string newtext, char splitChar)
		{
			int endOfLastFind = 0;
			int positionOfSplitChar = newtext.IndexOf(splitChar);
			while (positionOfSplitChar != -1)
			{
				string text = newtext.Substring(endOfLastFind, positionOfSplitChar - endOfLastFind).Trim();
				yield return text;
				endOfLastFind = positionOfSplitChar + 1;
				positionOfSplitChar = newtext.IndexOf(splitChar, endOfLastFind);
			}

			string lastText = newtext.Substring(endOfLastFind);
			yield return lastText;
		}

		private static GCodeMemoryFile ParseFileContents(string gCodeString,
			Vector4 maxAccelerationMmPerS2,
			Vector4 maxVelocityMmPerS,
			Vector4 velocitySameAsStopMmPerS,
			Vector4 speedMultiplier,
			CancellationToken cancellationToken, Action<double, string> progressReporter)
		{
			if (gCodeString == null)
			{
				return null;
			}

			Stopwatch loadTime = Stopwatch.StartNew();

			Stopwatch maxProgressReport = new Stopwatch();
			maxProgressReport.Start();
			PrinterMachineInstruction machineInstructionForLine = new PrinterMachineInstruction("None");

			bool gcodeHasExplicitLayerChangeInfo = false;
			if (gCodeString.Contains("LAYER:")
				|| gCodeString.Contains("; layer"))
			{
				gcodeHasExplicitLayerChangeInfo = true;
			}

			PrinterMachineInstruction previousInstruction = null;
			var speeds = new HashSet<float>();

			GCodeMemoryFile loadedGCodeFile = new GCodeMemoryFile(gcodeHasExplicitLayerChangeInfo);

			// Add the first start index (of 0)
			loadedGCodeFile.IndexOfLayerStart.Add(0);

			int crCount = CountNumLines(gCodeString);
			int lineIndex = 0;
			foreach (string outputString in CustomSplit(gCodeString, '\n'))
			{
				string lineString = outputString.Trim();
				machineInstructionForLine = new PrinterMachineInstruction(lineString, machineInstructionForLine, false);

				if (lineString.Length > 0)
				{
					switch (lineString[0])
					{
						case 'G':
							loadedGCodeFile.ParseGLine(lineString, machineInstructionForLine);
							break;

						case 'M':
							loadedGCodeFile.ParseMLine(lineString, machineInstructionForLine);
							break;

						case 'T':
							double extruderIndex = 0;
							if (GetFirstNumberAfter("T", lineString, ref extruderIndex))
							{
								machineInstructionForLine.ToolIndex = (int)extruderIndex;
							}
							break;

						case ';':
							if (gcodeHasExplicitLayerChangeInfo && IsLayerChange(lineString))
							{
								// The first "layer" statement in the gcode file is after the start gcode and we ignore
								// it because we already added a marker for the start of the file (before start gcode)
								if (!loadedGCodeFile.foundFirstLayerMarker)
								{
									loadedGCodeFile.foundFirstLayerMarker = true;
								}
								else
								{
									loadedGCodeFile.IndexOfLayerStart.Add(loadedGCodeFile.GCodeCommandQueue.Count);
								}
							}
							else if (lineString.StartsWith("; LAYER_HEIGHT:"))
							{
								double layerWidth = 0;
								if (GetFirstNumberAfter("LAYER_HEIGHT:", lineString, ref layerWidth, 0, ""))
								{
									loadedGCodeFile.layerHeights.Add(layerWidth);
								}
							}
							break;

						case '@':
							break;

						default:
							break;
					}
				}

				loadedGCodeFile.GCodeCommandQueue.Add(machineInstructionForLine);

				// Accumulate speeds for extruded moves
				if (previousInstruction != null
					&& machineInstructionForLine.EPosition > previousInstruction.EPosition
					&& (machineInstructionForLine.Line.IndexOf('X') != -1 || machineInstructionForLine.Line.IndexOf('Y') != -1))
				{
					speeds.Add((float)machineInstructionForLine.FeedRate);
				}

				if (progressReporter != null && maxProgressReport.ElapsedMilliseconds > 200)
				{
					progressReporter((double)lineIndex / crCount / 2, "");

					if (cancellationToken.IsCancellationRequested)
					{
						return null;
					}

					maxProgressReport.Restart();
				}

				previousInstruction = machineInstructionForLine;

				lineIndex++;
			}

			loadedGCodeFile.AnalyzeGCodeLines(cancellationToken, progressReporter,
				maxAccelerationMmPerS2,
				maxVelocityMmPerS,
				velocitySameAsStopMmPerS,
				speedMultiplier);

			loadedGCodeFile.Speeds = speeds;

			loadTime.Stop();
			Console.WriteLine("Time To Load Seconds: {0:0.00}".FormatWith(loadTime.Elapsed.TotalSeconds));

			return loadedGCodeFile;
		}

		private void AnalyzeGCodeLines(CancellationToken cancellationToken, Action<double, string> progressReporter,
			Vector4 maxAccelerationMmPerS2,
			Vector4 maxVelocityMmPerS,
			Vector4 velocitySameAsStopMmPerS,
			Vector4 speedMultiplier)
		{
			double feedRateMmPerMin = 0;
			Vector3 lastPrinterPosition = new Vector3();
			double lastEPosition = 0;

			Stopwatch maxProgressReport = new Stopwatch();
			maxProgressReport.Start();

			int currentTool = 0;

			for (int lineIndex = 0; lineIndex < GCodeCommandQueue.Count; lineIndex++)
			{
				PrinterMachineInstruction instruction = GCodeCommandQueue[lineIndex];
				string line = instruction.Line;
				Vector3 deltaPositionThisLine = new Vector3();
				double deltaEPositionThisLine = 0;
				string lineToParse = line.ToUpper().Trim();
				if (lineToParse.StartsWith("G0") || lineToParse.StartsWith("G1"))
				{
					double newFeedRateMmPerMin = 0;
					if (GetFirstNumberAfter("F", lineToParse, ref newFeedRateMmPerMin))
					{
						feedRateMmPerMin = newFeedRateMmPerMin;
					}

					Vector3 attemptedDestination = lastPrinterPosition;
					GetFirstNumberAfter("X", lineToParse, ref attemptedDestination.X);
					GetFirstNumberAfter("Y", lineToParse, ref attemptedDestination.Y);
					GetFirstNumberAfter("Z", lineToParse, ref attemptedDestination.Z);

					double ePosition = lastEPosition;
					GetFirstNumberAfter("E", lineToParse, ref ePosition);

					deltaPositionThisLine = attemptedDestination - lastPrinterPosition;
					deltaEPositionThisLine = Math.Abs(ePosition - lastEPosition);

					lastPrinterPosition = attemptedDestination;
					lastEPosition = ePosition;
				}
				else if (lineToParse.StartsWith("G92"))
				{
					double ePosition = 0;
					if (GetFirstNumberAfter("E", lineToParse, ref ePosition))
					{
						lastEPosition = ePosition;
					}
				}

				if (instruction.ToolIndex != currentTool)
				{
					toolChanges.Add(lineIndex);
					currentTool = instruction.ToolIndex;
				}

				if (feedRateMmPerMin > 0)
				{
					var timeForE = Estimator.GetSecondsForMovement(deltaEPositionThisLine,
						feedRateMmPerMin / 60.0,
						maxAccelerationMmPerS2[3],
						maxVelocityMmPerS[3],
						velocitySameAsStopMmPerS[3],
						speedMultiplier[3]);

					var timeForPosition = Estimator.GetSecondsForMovement(deltaPositionThisLine,
						feedRateMmPerMin / 60.0,
						new Vector3(maxAccelerationMmPerS2),
						new Vector3(maxVelocityMmPerS),
						new Vector3(velocitySameAsStopMmPerS),
						new Vector3(speedMultiplier));

					instruction.SecondsThisLine = (float)Math.Max(timeForE, timeForPosition);
				}

				if (progressReporter != null && maxProgressReport.ElapsedMilliseconds > 200)
				{
					progressReporter(((double)lineIndex / GCodeCommandQueue.Count / 2) + .5, "");
					if (cancellationToken.IsCancellationRequested)
					{
						return;
					}

					maxProgressReport.Restart();
				}
			}

			double accumulatedTime = 0;
			for (int i = GCodeCommandQueue.Count - 1; i >= 0; i--)
			{
				PrinterMachineInstruction line = GCodeCommandQueue[i];
				accumulatedTime += line.SecondsThisLine;
				line.SecondsToEndFromHere = (float)accumulatedTime;
			}
		}

		private bool FindDiameter(int lineIndex, ref double filamentDiameterCache)
		{
			if (GetFirstNumberAfter("filamentDiameter = ", GCodeCommandQueue[lineIndex].Line, ref filamentDiameterCache, 0, ""))
			{
				return true;
			}

			if (GetFirstNumberAfter("; filament_diameter = ", GCodeCommandQueue[lineIndex].Line, ref filamentDiameterCache, 0, ""))
			{
				return true;
			}

			return false;
		}

		private void ParseGLine(string lineString, PrinterMachineInstruction processingMachineState)
		{
			// take off any comments before we check its length
			int commentIndex = lineString.IndexOf(';');
			if (commentIndex != -1)
			{
				lineString = lineString.Substring(0, commentIndex);
			}

			string[] splitOnSpace = lineString.Split(' ');
			string onlyNumber = splitOnSpace[0].Substring(1).Trim();
			switch (onlyNumber)
			{
				case "0":
					goto case "1";

				case "4":
				case "04":
					// wait a given number of milliseconds
					break;

				case "1":
					// get the x y z to move to
					{
						double ePosition = processingMachineState.EPosition;
						var position = processingMachineState.Position;
						if (processingMachineState.MovementType == PrinterMachineInstruction.MovementTypes.Relative)
						{
							position = Vector3.Zero;
							ePosition = 0;
						}

						GCodeFile.GetFirstNumberAfter("X", lineString, ref position.X);
						GCodeFile.GetFirstNumberAfter("Y", lineString, ref position.Y);
						GCodeFile.GetFirstNumberAfter("Z", lineString, ref position.Z);
						GCodeFile.GetFirstNumberAfter("E", lineString, ref ePosition);

						double feedrate = 0;
						if (GCodeFile.GetFirstNumberAfter("F", lineString, ref feedrate))
						{
							processingMachineState.FeedRate = (float)feedrate;
						}

						if (processingMachineState.MovementType == PrinterMachineInstruction.MovementTypes.Absolute)
						{
							processingMachineState.Position = position;
							processingMachineState.EPosition = (float)ePosition;
						}
						else
						{
							processingMachineState.Position += position;
							processingMachineState.EPosition += (float)ePosition;
						}
					}

					if (!gcodeHasExplicitLayerChangeInfo)
					{
						if (processingMachineState.Z != parsingLastZ || IndexOfLayerStart.Count == 0)
						{
							// if we changed z or there is a movement and we have never started a layer index
							IndexOfLayerStart.Add(GCodeCommandQueue.Count);
						}
					}
					parsingLastZ = processingMachineState.Position.Z;
					break;

				case "10": // firmware retract
					break;

				case "11": // firmware unretract
					break;

				case "21":
					// set to metric
					break;

				case "28":
					// G28 	Return to home position (machine zero, aka machine reference point)
					break;

				case "29":
					// G29 Probe the z-bed in 3 places
					break;

				case "30":
					// G30 Probe z in current position
					break;

				case "90": // G90 is Absolute Distance Mode
					processingMachineState.MovementType = PrinterMachineInstruction.MovementTypes.Absolute;
					break;

				case "91": // G91 is Incremental Distance Mode
					processingMachineState.MovementType = PrinterMachineInstruction.MovementTypes.Relative;
					break;

				case "92":
					{
						// set current head position values (used to reset origin)
						double value = 0;
						if (GCodeFile.GetFirstNumberAfter("X", lineString, ref value))
						{
							processingMachineState.PositionSet |= PositionSet.X;
							processingMachineState.X = value;
						}
						if (GCodeFile.GetFirstNumberAfter("Y", lineString, ref value))
						{
							processingMachineState.PositionSet |= PositionSet.Y;
							processingMachineState.Y = value;
						}
						if (GCodeFile.GetFirstNumberAfter("Z", lineString, ref value))
						{
							processingMachineState.PositionSet |= PositionSet.Z;
							processingMachineState.Z = value;
						}
						if (GCodeFile.GetFirstNumberAfter("E", lineString, ref value))
						{
							processingMachineState.PositionSet |= PositionSet.E;
							processingMachineState.EPosition = (float)value;
						}
					}
					break;

				case "130":
					//Set Digital Potentiometer value
					break;

				case "161":
					// home x,y axis minimum
					break;

				case "162":
					// home z axis maximum
					break;

				default:
					break;
			}
		}

		private void ParseMLine(string lineString, PrinterMachineInstruction processingMachineState)
		{
			// take off any comments before we check its length
			int commentIndex = lineString.IndexOf(';');
			if (commentIndex != -1)
			{
				lineString = lineString.Substring(0, commentIndex);
			}

			string[] splitOnSpace = lineString.Split(' ');
			switch (splitOnSpace[0].Substring(1).Trim())
			{
				case "01":
					// show a message?
					break;

				case "6":
					// wait for tool to heat up (wait for condition?)
					break;

				case "101":
					// extrude on, forward
					break;

				case "18":
					// turn off steppers
					break;

				case "42":
					// Stop on material exhausted / Switch I/O pin
					break;

				case "72":
					// makerbot, Play tone or song
					break;

				case "73":
					// makerbot, Manually set build percentage
					break;

				case "82":
					// set extruder to absolute mode
					break;

				case "83":
					//Set extruder to relative mode
					break;

				case "84":
					// lineString = "M84     ; disable motors\r"
					break;

				case "92":
					// set steps per mm
					break;

				case "102":
					// extrude on reverse
					break;

				case "103":
					// extrude off
					break;

				case "104":
					// set extruder temperature
					break;

				case "105":
					// M105 Custom code for temperature reading. (Not used)
					break;

				case "106":
					// turn fan on
					break;

				case "107":
					// turn fan off
					break;

				case "108":
					// set extruder speed
					break;

				case "109":
					// set heated platform temperature
					break;

				case "114":
					break;

				case "117":
					// in Marlin: Display Message
					break;

				case "126":
					// enable fan (makerbot)
					break;

				case "127":
					// disable fan (makerbot)
					break;

				case "132":
					// recall stored home offsets for axis xyzab
					break;

				case "133":
					// MakerBot wait for toolhead to heat
					break;

				case "134":
					// MakerBot wait for platform to reach target temp
					break;

				case "135":
					// MakerBot change toolhead
					break;

				case "140":
					// set bed temperature
					break;

				case "190":
					// wait for bed temperature to be reached
					break;

				case "200":
					// M200 sets the filament diameter.
					break;

				case "201":
					// set axis acceleration
					break;

				case "204": // - Set default acceleration
					break;

				case "207": // M207: calibrate z axis by detecting z max length
					break;

				case "208": // M208: set axis max travel
					break;

				case "209": // M209: enable automatic retract
					break;

				case "210": // Set homing rate
					break;

				case "226": // user request pause
					break;

				case "227": // Enable Automatic Reverse and Prime
					break;

				case "301":
					break;

				case "400": // Wait for current moves to finish
					break;

				case "565": // M565: Set Z probe offset
					break;

				case "1200"://M1200 Makerbot Fake gCode command for start build notification
					break;

				case "1201"://M1201 Makerbot Fake gCode command for end build notification
					break;

				case "1202"://M1202 Makerbot Fake gCode command for reset board
					break;

				default:
					break;
			}
		}
	}
}