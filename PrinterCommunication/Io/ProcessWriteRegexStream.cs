﻿/*
Copyright (c) 2015, Lars Brubaker
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
using System.Collections.Generic;
using System.Text.RegularExpressions;
using MatterHackers.Agg.UI;
using MatterHackers.MatterControl.SlicerConfiguration;
using MatterHackers.VectorMath;

namespace MatterHackers.MatterControl.PrinterCommunication.Io
{
	public class RegReplace
	{
		public Regex Regex { get; set; }
		public string Replacement { get; set; }
	}

	public class ProcessWriteRegexStream : GCodeStreamProxy
	{
		static Regex getQuotedParts = new Regex(@"([""'])(\\?.)*?\1", RegexOptions.Compiled);
		QueuedCommandsStream queueStream;

		public ProcessWriteRegexStream(GCodeStream internalStream, QueuedCommandsStream queueStream)
			: base(internalStream)
		{
			this.queueStream = queueStream;
		}

		public override string ReadLine()
		{
			var baseLine = base.ReadLine();

			if (baseLine == null)
			{
				return null;
			}

			var lines = ProcessWriteRegEx(baseLine);
			for (int i = 1; i < lines.Count; i++)
			{
				queueStream.Add(lines[i]);
			}

			return lines[0];
		}

		static string write_regex = "";
		static private List<RegReplace> WriteLineReplacements = new List<RegReplace>();

		public static List<string> ProcessWriteRegEx(string lineToWrite)
		{
			lock (WriteLineReplacements)
			{
				if (write_regex != ActiveSliceSettings.Instance.GetValue(SettingsKey.write_regex))
				{
					WriteLineReplacements.Clear();
					string splitString = "\\n";
					write_regex = ActiveSliceSettings.Instance.GetValue(SettingsKey.write_regex);
					foreach (string regExLine in write_regex.Split(new string[] { splitString }, StringSplitOptions.RemoveEmptyEntries))
					{
						var matches = getQuotedParts.Matches(regExLine);
						if (matches.Count == 2)
						{
							var search = matches[0].Value.Substring(1, matches[0].Value.Length - 2);
							var replace = matches[1].Value.Substring(1, matches[1].Value.Length - 2);
							WriteLineReplacements.Add(new RegReplace()
							{
								Regex = new Regex(search, RegexOptions.Compiled),
								Replacement = replace
							});
						}
					}
				}
			}

			var linesToWrite = new List<string>();
			linesToWrite.Add(lineToWrite);

			var addedLines = new List<string>();
			for (int i = 0; i < linesToWrite.Count; i++)
			{
				foreach (var item in WriteLineReplacements)
				{
					var splitReplacement = item.Replacement.Split(',');
					if (splitReplacement.Length > 0)
					{
						if (item.Regex.IsMatch(lineToWrite))
						{
							// replace on the first replacement group only
							var replacedString = item.Regex.Replace(lineToWrite, splitReplacement[0]);
							linesToWrite[i] = replacedString;
							// add in the othre replacement groups
							for (int j = 1; j < splitReplacement.Length; j++)
							{
								addedLines.Add(splitReplacement[j]);
							}
						}
					}
				}
			}

			linesToWrite.AddRange(addedLines);

			return linesToWrite;
		}
	}
}