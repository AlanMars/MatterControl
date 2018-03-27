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
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using MatterHackers.Agg;
using MatterHackers.Agg.Image;
using MatterHackers.Agg.Platform;
using MatterHackers.Agg.UI;
using MatterHackers.DataConverters3D;
using MatterHackers.Localizations;
using MatterHackers.MatterControl.CustomWidgets;
using MatterHackers.MatterControl.DesignTools.Operations;
using MatterHackers.MatterControl.PartPreviewWindow;
using MatterHackers.MatterControl.SlicerConfiguration;
using MatterHackers.VectorMath;

namespace MatterHackers.MatterControl.DesignTools
{

	public class PublicPropertyEditor : IObject3DEditor
	{
		private IObject3D item;
		private View3DWidget view3DWidget;
		public string Name => "Property Editor";

		public bool Unlocked { get; } = true;

		public IEnumerable<Type> SupportedTypes() => new Type[] { typeof(IRebuildable) };

		private Dictionary<string, GuiWidget> editRows = new Dictionary<string, GuiWidget>();

		private static Type[] allowedTypes =
		{
			typeof(double), typeof(int), typeof(char), typeof(string), typeof(bool),
			typeof(Vector2), typeof(Vector3),
			typeof(DirectionVector), typeof(DirectionAxis),
			typeof(ImageObject3D)
		};

		private static Type IObject3DType = typeof(IObject3D);

		public const BindingFlags OwnedPropertiesOnly = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

		public GuiWidget Create(IObject3D item, View3DWidget view3DWidget, ThemeConfig theme)
		{
			this.view3DWidget = view3DWidget;
			this.item = item;

			var mainContainer = new FlowLayoutWidget(FlowDirection.TopToBottom)
			{
				HAnchor = HAnchor.Stretch
			};

			if(item is IEditorDraw editorDraw)
			{
				view3DWidget.InteractionLayer.DrawGlOpaqueContent += editorDraw.DrawEditor;
				mainContainer.Closed += (s, e) =>
				{
					view3DWidget.InteractionLayer.DrawGlOpaqueContent -= editorDraw.DrawEditor;
				};
			}

			if (this.item != null)
			{
				ModifyObject(view3DWidget, mainContainer, theme);
			}

			return mainContainer;
		}

		public GuiWidget GetEditRow(string propertyName)
		{
			GuiWidget value;
			if (editRows.TryGetValue(propertyName, out value))
			{
				return value;
			}

			return null;
		}

		private static FlowLayoutWidget CreateSettingsRow(string labelText, string toolTipText = null)
		{
			var rowContainer = new FlowLayoutWidget(FlowDirection.LeftToRight)
			{
				HAnchor = HAnchor.Stretch,
				Padding = new BorderDouble(5),
				ToolTipText = toolTipText
			};

			var label = new TextWidget(labelText + ":", pointSize: 11, textColor: ActiveTheme.Instance.PrimaryTextColor)
			{
				Margin = new BorderDouble(0, 0, 3, 0),
				VAnchor = VAnchor.Center
			};
			rowContainer.AddChild(label);

			rowContainer.AddChild(new HorizontalSpacer());

			return rowContainer;
		}

		private string GetDisplayName(PropertyInfo prop)
		{
			var nameAttribute = prop.GetCustomAttributes(true).OfType<DisplayNameAttribute>().FirstOrDefault();
			return nameAttribute?.DisplayName ?? prop.Name.SplitCamelCase();
		}

		private string GetDescription(PropertyInfo prop)
		{
			var nameAttribute = prop.GetCustomAttributes(true).OfType<DescriptionAttribute>().FirstOrDefault();
			return nameAttribute?.Description ?? null;
		}

		private void ModifyObject(View3DWidget view3DWidget, FlowLayoutWidget editControlsContainer, ThemeConfig theme)
		{
			var undoBuffer = view3DWidget.sceneContext.Scene.UndoBuffer;
			editRows.Clear();

			var rebuildable = item as IRebuildable;
			var propertyGridModifier = item as IPropertyGridModifier;

			var editableProperties = this.item.GetType().GetProperties(OwnedPropertiesOnly)
				.Where(pi => (allowedTypes.Contains(pi.PropertyType) || pi.PropertyType.IsEnum)
					&& pi.GetGetMethod() != null
					&& pi.GetSetMethod() != null)
				.Select(p => new
				{
					Value = p.GetGetMethod().Invoke(this.item, null),
					DisplayName = GetDisplayName(p),
					Description = GetDescription(p),
					PropertyType = p.PropertyType,
					PropertyInfo = p
				});

			AddWebPageLinkIfRequired(editControlsContainer, theme);
			AddUnlockLinkIfRequired(editControlsContainer, theme);

			foreach (var property in editableProperties)
			{
				GuiWidget rowContainer = null;

				// create a double editor
				if (property.Value is double doubleValue)
				{
					rowContainer = CreateSettingsRow(property.DisplayName.Localize());

					var field = new DoubleField();
					field.Initialize(0);
					field.DoubleValue = doubleValue;
					field.ValueChanged += (s, e) =>
					{
						property.PropertyInfo.GetSetMethod().Invoke(this.item, new Object[] { field.DoubleValue });
						rebuildable?.Rebuild(undoBuffer);
						propertyGridModifier?.UpdateControls(this);
					};

					rowContainer.AddChild(field.Content);
					editControlsContainer.AddChild(rowContainer);
				}
				else if (property.Value is Vector2 vector2)
				{
					rowContainer = CreateSettingsRow(property.DisplayName.Localize());

					var field = new Vector2Field();
					field.Initialize(0);
					field.Vector2 = vector2;
					field.ValueChanged += (s, e) =>
					{
						property.PropertyInfo.GetSetMethod().Invoke(this.item, new Object[] { field.Vector2 });
						rebuildable?.Rebuild(undoBuffer);
						propertyGridModifier?.UpdateControls(this);
					};

					rowContainer.AddChild(field.Content);
					editControlsContainer.AddChild(rowContainer);
				}
				else if (property.Value is Vector3 vector3)
				{
					rowContainer = CreateSettingsRow(property.DisplayName.Localize());

					var field = new Vector3Field();
					field.Initialize(0);
					field.Vector3 = vector3;
					field.ValueChanged += (s, e) =>
					{
						property.PropertyInfo.GetSetMethod().Invoke(this.item, new Object[] { field.Vector3 });
						rebuildable?.Rebuild(undoBuffer);
						propertyGridModifier?.UpdateControls(this);
					};

					rowContainer.AddChild(field.Content);
					editControlsContainer.AddChild(rowContainer);
				}
				else if (property.Value is DirectionVector directionVector)
				{
					bool simpleEdit = true;
					if (simpleEdit)
					{
						rowContainer = CreateSettingsRow(property.DisplayName.Localize());

						var dropDownList = new DropDownList("Name".Localize(), theme.Colors.PrimaryTextColor, Direction.Down, pointSize: theme.DefaultFontSize);

						var orderedItems = new string[] { "Right", "Back", "Up" };

						foreach (var orderItem in orderedItems)
						{
							MenuItem newItem = dropDownList.AddItem(orderItem);

							var localOredrItem = orderItem;
							newItem.Selected += (sender, e) =>
							{
								switch (dropDownList.SelectedValue)
								{
									case "Right":
										property.PropertyInfo.GetSetMethod().Invoke(this.item, new Object[] { new DirectionVector() { Normal = Vector3.UnitX } });
										break;
									case "Back":
										property.PropertyInfo.GetSetMethod().Invoke(this.item, new Object[] { new DirectionVector() { Normal = Vector3.UnitY } });
										break;
									case "Up":
										property.PropertyInfo.GetSetMethod().Invoke(this.item, new Object[] { new DirectionVector() { Normal = Vector3.UnitZ } });
										break;
								}

								rebuildable?.Rebuild(undoBuffer);
								propertyGridModifier?.UpdateControls(this);
							};
						}

						dropDownList.SelectedLabel = "Right";
						rowContainer.AddChild(dropDownList);
						editControlsContainer.AddChild(rowContainer);
					}
					else // edit the vector
					{
						rowContainer = CreateSettingsRow(property.DisplayName.Localize());

						var field = new Vector3Field();
						field.Initialize(0);
						field.Vector3 = directionVector.Normal;
						field.ValueChanged += (s, e) =>
						{
							property.PropertyInfo.GetSetMethod().Invoke(this.item, new Object[] { new DirectionVector() { Normal = field.Vector3 } });
							rebuildable?.Rebuild(undoBuffer);
							propertyGridModifier?.UpdateControls(this);
						};

						rowContainer.AddChild(field.Content);
						editControlsContainer.AddChild(rowContainer);
					}
				}
				else if (property.Value is DirectionAxis directionAxis)
				{
					bool simpleAxis = true;

					if (simpleAxis)
					{
						// the direction axis
						// the distance from the center of the part
						// create a double editor
						rowContainer = CreateSettingsRow(property.DisplayName.Localize());

						var field = new DoubleField();
						field.Initialize(0);
						field.DoubleValue = directionAxis.Origin.X - item.Children.First().GetAxisAlignedBoundingBox().Center.X;
						field.ValueChanged += (s, e) =>
						{
							property.PropertyInfo.GetSetMethod().Invoke(this.item, new Object[]
								{
									new DirectionAxis()
									{
										Normal = Vector3.UnitZ, Origin = item.Children.First().GetAxisAlignedBoundingBox().Center + new Vector3(field.DoubleValue, 0, 0)
									}
								});
							rebuildable?.Rebuild(undoBuffer);
							propertyGridModifier?.UpdateControls(this);
						};

						rowContainer.AddChild(field.Content);
						editControlsContainer.AddChild(rowContainer);

						// update tihs when changed
						EventHandler updateData = (object s, EventArgs e) =>
						{
							field.DoubleValue = ((DirectionAxis)property.PropertyInfo.GetGetMethod().Invoke(this.item, null)).Origin.X - item.Children.First().GetAxisAlignedBoundingBox().Center.X;
						};
						item.Invalidated += updateData;
						editControlsContainer.Closed += (s, e) =>
						{
							item.Invalidated -= updateData;
						};
					}
					else
					{
						// add in the position
						FlowLayoutWidget originRowContainer = CreateSettingsRow(property.DisplayName.Localize());

						var originField = new Vector3Field();
						originField.Initialize(0);
						originField.Vector3 = directionAxis.Origin;

						var normalField = new Vector3Field();
						normalField.Initialize(0);
						normalField.Vector3 = directionAxis.Normal;

						originField.ValueChanged += (s, e) =>
						{
							property.PropertyInfo.GetSetMethod().Invoke(this.item, new Object[] { new DirectionAxis() { Origin = originField.Vector3, Normal = normalField.Vector3 } });
							rebuildable?.Rebuild(undoBuffer);
							propertyGridModifier?.UpdateControls(this);
						};

						originRowContainer.AddChild(originField.Content);
						editControlsContainer.AddChild(originRowContainer);

						// add in the direction
						FlowLayoutWidget directionRowContainer = CreateSettingsRow(property.DisplayName.Localize());

						normalField.ValueChanged += (s, e) =>
						{
							property.PropertyInfo.GetSetMethod().Invoke(this.item, new Object[] { new DirectionAxis() { Origin = originField.Vector3, Normal = normalField.Vector3 } });
							rebuildable?.Rebuild(undoBuffer);
							propertyGridModifier?.UpdateControls(this);
						};

						directionRowContainer.AddChild(normalField.Content);
						editControlsContainer.AddChild(directionRowContainer);

						// update tihs when changed
						EventHandler updateData = (object s, EventArgs e) =>
						{
							originField.Vector3 = ((DirectionAxis)property.PropertyInfo.GetGetMethod().Invoke(this.item, null)).Origin;
						};
						item.Invalidated += updateData;
						editControlsContainer.Closed += (s, e) =>
						{
							item.Invalidated -= updateData;
						};
					}
				}
				// create a int editor
				else if (property.Value is int intValue)
				{
					rowContainer = CreateSettingsRow(property.DisplayName.Localize());

					var field = new IntField();
					field.Initialize(0);
					field.IntValue = intValue;
					field.ValueChanged += (s, e) =>
					{
						property.PropertyInfo.GetSetMethod().Invoke(this.item, new Object[] { field.IntValue });
						rebuildable?.Rebuild(undoBuffer);
						propertyGridModifier?.UpdateControls(this);
					};

					rowContainer.AddChild(field.Content);
					editControlsContainer.AddChild(rowContainer);
				}
				// create a bool editor
				else if (property.Value is bool boolValue)
				{
					rowContainer = CreateSettingsRow(property.DisplayName.Localize(), property.Description.Localize());

					var field = new ToggleboxField(ApplicationController.Instance.Theme.Colors.PrimaryTextColor);
					field.Initialize(0);
					field.Checked = boolValue;
					field.ValueChanged += (s, e) =>
					{
						property.PropertyInfo.GetSetMethod().Invoke(this.item, new Object[] { field.Checked });
						rebuildable?.Rebuild(undoBuffer);
						propertyGridModifier?.UpdateControls(this);
					};

					rowContainer.AddChild(field.Content);
					editControlsContainer.AddChild(rowContainer);
				}
				// create a string editor
				else if (property.Value is string stringValue)
				{
					rowContainer = CreateSettingsRow(property.DisplayName.Localize());
					var textEditWidget = new MHTextEditWidget(stringValue, pixelWidth: 150 * GuiWidget.DeviceScale)
					{
						SelectAllOnFocus = true,
						VAnchor = VAnchor.Center
					};
					textEditWidget.ActualTextEditWidget.EditComplete += (s, e) =>
					{
						property.PropertyInfo.GetSetMethod().Invoke(this.item, new Object[] { textEditWidget.Text });
						rebuildable?.Rebuild(undoBuffer);
						propertyGridModifier?.UpdateControls(this);
					};
					rowContainer.AddChild(textEditWidget);
					editControlsContainer.AddChild(rowContainer);
				}
				// create a char editor
				else if (property.Value is char charValue)
				{
					rowContainer = CreateSettingsRow(property.DisplayName.Localize());
					var textEditWidget = new MHTextEditWidget(charValue.ToString(), pixelWidth: 150 * GuiWidget.DeviceScale)
					{
						SelectAllOnFocus = true,
						VAnchor = VAnchor.Center
					};
					textEditWidget.ActualTextEditWidget.EditComplete += (s, e) =>
					{
						if (textEditWidget.Text.Length < 1)
						{
							textEditWidget.Text = "a";
						}
						if (textEditWidget.Text.Length > 1)
						{
							textEditWidget.Text = textEditWidget.Text.Substring(0, 1);
						}
						property.PropertyInfo.GetSetMethod().Invoke(this.item, new Object[] { textEditWidget.Text[0] });
						rebuildable?.Rebuild(undoBuffer);
						propertyGridModifier?.UpdateControls(this);
					};
					rowContainer.AddChild(textEditWidget);
					editControlsContainer.AddChild(rowContainer);
				}
				// create an enum editor
				else if (property.PropertyType.IsEnum)
				{
					rowContainer = CreateEnumEditor(rebuildable,
							property.PropertyInfo, property.PropertyType, property.Value, property.DisplayName,
							theme, undoBuffer);
					editControlsContainer.AddChild(rowContainer);
				}
				// Use known IObject3D editors
				else if (property.Value is IObject3D object3D
					&& ApplicationController.Instance.GetEditorsForType(property.PropertyType)?.FirstOrDefault() is IObject3DEditor editor)
				{
					rowContainer = editor.Create( object3D, view3DWidget, theme);
					editControlsContainer.AddChild(rowContainer);
				}

				// remember the row name and widget
				editRows.Add(property.PropertyInfo.Name, rowContainer);
			}

			var hideUpdate = item.GetType().GetCustomAttributes(typeof(HideUpdateButtonAttribute), true).FirstOrDefault() as HideUpdateButtonAttribute;
			if (hideUpdate == null)
			{
				var updateButton = theme.ButtonFactory.Generate("Update".Localize());
				updateButton.Margin = new BorderDouble(5);
				updateButton.HAnchor = HAnchor.Right;
				updateButton.Click += (s, e) =>
				{
					rebuildable?.Rebuild(undoBuffer);
				};
				editControlsContainer.AddChild(updateButton);
			}
			// make sure the ui is set right to start
			propertyGridModifier?.UpdateControls(this);
		}

		private void AddUnlockLinkIfRequired(FlowLayoutWidget editControlsContainer, ThemeConfig theme)
		{
			var unlockLink = item.GetType().GetCustomAttributes(typeof(UnlockLinkAttribute), true).FirstOrDefault() as UnlockLinkAttribute;
			if (unlockLink != null
				&& !string.IsNullOrEmpty(unlockLink.UnlockPageLink)
				&& !item.Persistable)
			{
				var row = CreateSettingsRow(item.Persistable ? "Registered".Localize() : "Demo Mode".Localize());

				Button detailsLink = theme.ButtonFactory.Generate("Unlock".Localize(), AggContext.StaticData.LoadIcon("locked.png", 16, 16));
				detailsLink.BackgroundColor = theme.Colors.PrimaryAccentColor.AdjustContrast(theme.Colors.PrimaryTextColor, 8).ToColor();
				detailsLink.Margin = new BorderDouble(5);
				detailsLink.Click += (s, e) =>
				{
					ApplicationController.Instance.LaunchBrowser(UnlockLinkAttribute.UnlockPageBaseUrl + unlockLink.UnlockPageLink);
				};
				row.AddChild(detailsLink);
				editControlsContainer.AddChild(row);
			}
		}

		private void AddWebPageLinkIfRequired(FlowLayoutWidget editControlsContainer, ThemeConfig theme)
		{
			var unlockLink = item.GetType().GetCustomAttributes(typeof(WebPageLinkAttribute), true).FirstOrDefault() as WebPageLinkAttribute;
			if (unlockLink != null)
			{
				var row = CreateSettingsRow(unlockLink.Name.Localize());

				Button detailsLink = theme.ButtonFactory.Generate("Open", AggContext.StaticData.LoadIcon("internet.png", 16, 16));
				detailsLink.Margin = new BorderDouble(5);
				detailsLink.Click += (s, e) =>
				{
					ApplicationController.Instance.LaunchBrowser(unlockLink.Url);
				};
				row.AddChild(detailsLink);
				editControlsContainer.AddChild(row);
			}
		}

		private GuiWidget CreateEnumEditor(IRebuildable item,
			PropertyInfo propertyInfo, Type propertyType, object value, string displayName,
			ThemeConfig theme,
			UndoBuffer undoBuffer)
		{
			var propertyGridModifier = item as IPropertyGridModifier;

			// Enum keyed on name to friendly name
			var enumItems = Enum.GetNames(propertyType).Select(enumName =>
			{
				return new
				{
					Key = enumName,
					Value = enumName.Replace('_', ' ')
				};
			});

			FlowLayoutWidget rowContainer = CreateSettingsRow(displayName);

			var iconsAttribute = propertyInfo.GetCustomAttributes(true).OfType<IconsAttribute>().FirstOrDefault();
			if (iconsAttribute != null)
			{
				int index = 0;
				foreach (var enumItem in enumItems)
				{
					var localIndex = index;
					ImageBuffer iconImage = null;
					var iconPath = iconsAttribute.IconPaths[localIndex];
					if (!string.IsNullOrWhiteSpace(iconPath))
					{
						if (iconsAttribute.Width > 0)
						{
							iconImage = AggContext.StaticData.LoadIcon(iconPath, iconsAttribute.Width, iconsAttribute.Height);
						}
						else
						{
							iconImage = AggContext.StaticData.LoadIcon(iconPath);
						}
						var radioButton = new RadioButton(new ImageWidget(iconImage));
						radioButton.ToolTipText = enumItem.Key;
						// set it if checked
						if (enumItem.Value == value.ToString())
						{
							radioButton.Checked = true;
							if (localIndex != 0
								|| !iconsAttribute.Item0IsNone)
							{
								radioButton.BackgroundColor = new Color(Color.Black, 100);
							}
						}

						rowContainer.AddChild(radioButton);

						var localItem = enumItem;
						radioButton.CheckedStateChanged += (sender, e) =>
						{
							if (radioButton.Checked)
							{
								propertyInfo.GetSetMethod().Invoke(
									this.item,
									new Object[] { Enum.Parse(propertyType, localItem.Key) });
								item?.Rebuild(undoBuffer);
								propertyGridModifier?.UpdateControls(this);
								if (localIndex != 0
									|| !iconsAttribute.Item0IsNone)
								{
									radioButton.BackgroundColor = new Color(Color.Black, 100);
								}
							}
							else
							{
								radioButton.BackgroundColor = Color.Transparent;
							}
						};
						index++;
					}
				}
			}
			else
			{
				var dropDownList = new DropDownList("Name".Localize(), theme.Colors.PrimaryTextColor, Direction.Down, pointSize: theme.DefaultFontSize);

				var sortableAttribute = propertyInfo.GetCustomAttributes(true).OfType<SortableAttribute>().FirstOrDefault();
				var orderedItems = sortableAttribute != null ? enumItems.OrderBy(n => n.Value) : enumItems;

				foreach (var orderItem in orderedItems)
				{
					MenuItem newItem = dropDownList.AddItem(orderItem.Value);

					var localOrderedItem = orderItem;
					newItem.Selected += (sender, e) =>
					{
						propertyInfo.GetSetMethod().Invoke(
							this.item,
							new Object[] { Enum.Parse(propertyType, localOrderedItem.Key) });
						item?.Rebuild(undoBuffer);
						propertyGridModifier?.UpdateControls(this);
					};
				}

				dropDownList.SelectedLabel = value.ToString().Replace('_', ' ');
				rowContainer.AddChild(dropDownList);
			}

			return rowContainer;
		}
	}
}