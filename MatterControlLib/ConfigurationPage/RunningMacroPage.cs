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

using System;
#if !__ANDROID__
using Markdig.Agg;
#endif
using MatterHackers.Agg;
using MatterHackers.Agg.Image;
using MatterHackers.Agg.UI;
using MatterHackers.Localizations;
using MatterHackers.MatterControl.SlicerConfiguration;

namespace MatterHackers.MatterControl.PrinterControls
{
	public class RunningMacroPage : DialogPage
	{
		private long startTimeMs;
		private ProgressBar progressBar;
		private RunningInterval runningInterval;

		private TextWidget progressBarText;

		private long timeToWaitMs;
		private PrinterConfig printer;

		public RunningMacroPage(PrinterConfig printer, MacroCommandData macroData, ThemeConfig theme)
			: base("Cancel")
		{
			this.printer = printer;
			this.WindowTitle = "Running Macro".Localize();
			this.HeaderText = macroData.title;

			if (macroData.showMaterialSelector)
			{
				ContentRow.AddChild(new PresetSelectorWidget(printer, "Material".Localize(), Color.Transparent, NamedSettingsLayers.Material, theme)
				{
					BackgroundColor = Color.Transparent,
					Margin = new BorderDouble(0, 0, 0, 15)
				});
			}

			printer.Connection.LineSent.RegisterEvent(LookForTempRequest, ref unregisterEvents);

			if (macroData.waitOk | macroData.expireTime > 0)
			{
				var okButton = theme.CreateDialogButton("Continue".Localize());
				okButton.Name = "Continue Button";
				okButton.Click += (s, e) =>
				{
					ContinueToNextPage = true;
					printer.Connection.MacroContinue();
				};

				this.AddPageAction(okButton);
			}

#if !__ANDROID__
			if (!string.IsNullOrEmpty(macroData.markdown))
			{
				var markdown = new MarkdownWidget(theme);

				markdown.Markdown = macroData.markdown;

				ContentRow.AddChild(markdown);
			}
#endif

			var holder = new FlowLayoutWidget();
			progressBar = new ProgressBar((int)(150 * GuiWidget.DeviceScale), (int)(15 * GuiWidget.DeviceScale))
			{
				FillColor = theme.PrimaryAccentColor,
				BorderColor = ActiveTheme.Instance.PrimaryTextColor,
				BackgroundColor = Color.White,
				Margin = new BorderDouble(3, 0, 0, 10),
			};
			progressBarText = new TextWidget("", pointSize: 10, textColor: ActiveTheme.Instance.PrimaryTextColor)
			{
				AutoExpandBoundsToText = true,
				Margin = new BorderDouble(5, 0, 0, 0),
			};
			holder.AddChild(progressBar);
			holder.AddChild(progressBarText);
			ContentRow.AddChild(holder);
			progressBar.Visible = false;

			if (macroData.countDown > 0)
			{
				timeToWaitMs = (long)(macroData.countDown * 1000);
				startTimeMs = UiThread.CurrentTimerMs;
				runningInterval = UiThread.SetInterval(CountDownTime, .2);
			}
		}

		protected override void OnCancel(out bool abortCancel)
		{
			printer.Connection.MacroCancel();
			abortCancel = false;
		}

		private EventHandler unregisterEvents;

		public class MacroCommandData
		{
			public bool waitOk = false;
			public string title = "";
			public bool showMaterialSelector = false;
			public double countDown = 0;
			public double expireTime = 0;
			public double expectedTemperature = 0;
			public string markdown = "";
		}

		public override void OnClosed(EventArgs e)
		{
			if (!ContinueToNextPage)
			{
				printer.Connection.MacroCancel();
			}
			unregisterEvents?.Invoke(this, null);

			base.OnClosed(e);
		}

		private void CountDownTime()
		{
			if(runningInterval != null)
			{
				runningInterval.Continue = !HasBeenClosed && progressBar.RatioComplete < 1;
				if(!runningInterval.Continue)
				{
					ContinueToNextPage = true;
				}
			}
			progressBar.Visible = true;
			long timeSinceStartMs = UiThread.CurrentTimerMs - startTimeMs;
			progressBar.RatioComplete = timeToWaitMs == 0 ? 1 : Math.Max(0, Math.Min(1, ((double)timeSinceStartMs / (double)timeToWaitMs)));
			int seconds = (int)((timeToWaitMs - (timeToWaitMs * (progressBar.RatioComplete))) / 1000);
			progressBarText.Text = $"Time Remaining: {seconds / 60:#0}:{seconds % 60:00}";
		}

		double startingTemp;

		public bool ContinueToNextPage { get; set; } = false;

		private void LookForTempRequest(object sender, EventArgs e)
		{
			var stringEvent = e as StringEventArgs;
			if(stringEvent != null
				&& stringEvent.Data.Contains("M104"))
			{
				startingTemp = printer.Connection.GetActualHotendTemperature(0);
				RunningInterval runningInterval = null;
				runningInterval = UiThread.SetInterval(() =>
				{
					runningInterval.Continue = !HasBeenClosed && progressBar.RatioComplete < 1;
					progressBar.Visible = true;
					double targetTemp = printer.Connection.GetTargetHotendTemperature(0);
					double actualTemp = printer.Connection.GetActualHotendTemperature(0);
					double totalDelta = targetTemp - startingTemp;
					double currentDelta = actualTemp - startingTemp;
					double ratioDone = totalDelta != 0 ? (currentDelta / totalDelta) : 1;
					progressBar.RatioComplete = Math.Min(Math.Max(0, ratioDone), 1);
					progressBarText.Text = $"Temperature: {actualTemp:0} / {targetTemp:0}";
				}, 1);
			}
		}
	}
}