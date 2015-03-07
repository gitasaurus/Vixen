﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows.Forms;
using System.Xml;
using Common.Controls;
using Common.Controls.Timeline;
using Common.Resources.Properties;
using NLog;
using Vixen;
using Vixen.Cache.Sequence;
using Vixen.Execution;
using Vixen.Execution.Context;
using Vixen.Module;
using Vixen.Module.App;
using VixenModules.App.Curves;
using VixenModules.App.LipSyncApp;
using VixenModules.Media.Audio;
using VixenModules.Effect.LipSync;
using Vixen.Module.Editor;
using Vixen.Module.Effect;
using Vixen.Module.Media;
using Vixen.Module.Timing;
using Vixen.Services;
using Vixen.Sys;
using Vixen.Sys.State;
using VixenModules.Analysis.BeatsAndBars;
using VixenModules.App.ColorGradients;
using VixenModules.Sequence.Timed;
using WeifenLuo.WinFormsUI.Docking;
using Element = Common.Controls.Timeline.Element;
using Timer = System.Windows.Forms.Timer;
using VixenModules.Property.Color;

namespace VixenModules.Editor.TimedSequenceEditor
{

	public partial class TimedSequenceEditorForm : Form, IEditorUserInterface, ITiming
	{

		#region Member Variables

		private static readonly Logger Logging = LogManager.GetCurrentClassLogger();

		// the sequence.
		private TimedSequence _sequence;

		// the program context we will be playing this sequence in: used to interact with the execution engine.
		private ISequenceContext _context;

		// the timing source this sequence will be executing against. Used to update times, etc.
		private ITiming _timingSource;

		// Delayed play countdown
		private int _delayCountDown;

		private readonly Timer _autoSaveTimer = new Timer();

		// Variables used by the add multiple effects dialog
		private int _amLastEffectCount;
		private TimeSpan _amLastStartTime;
		private TimeSpan _amLastDuration;
		private TimeSpan _amLastDurationBetween;

		// a mapping of effects in the sequence to the element that represent them in the grid.
		private Dictionary<EffectNode, Element> _effectNodeToElement;

		// a mapping of system elements to the (possibly multiple) rows that represent them in the grid.
		private Dictionary<ElementNode, List<Row>> _elementNodeToRows;

		// the default time for a sequence if one is loaded with 0 time
		private static readonly TimeSpan DefaultSequenceTime = TimeSpan.FromMinutes(1);

		// Undo manager
		private UndoManager _undoMgr;

		private TimeSpan? _mPrevPlaybackStart;
		private TimeSpan? _mPrevPlaybackEnd;

		private bool _mModified;

		private float _timingSpeed = 1;

		private float _timingChangeDelta = 0.25f;

		private static readonly DataFormats.Format ClipboardFormatName =
			DataFormats.GetFormat(typeof (TimelineElementsClipboardData).FullName);


		private readonly ContextMenuStrip _contextMenuStrip = new ContextMenuStrip();

		private string _settingsPath;
		private string _colorCollectionsPath;

		private CurveLibrary _curveLibrary;
		private ColorGradientLibrary _colorGradientLibrary;
		private LipSyncMapLibrary _library;
		private List<ColorCollection> _colorCollections = new List<ColorCollection>();

		//Used for color collections
		private static Random rnd = new Random();
		private PreCachingSequenceEngine _preCachingSequenceEngine;

		#endregion

		#region Constructor / Initialization

		public TimedSequenceEditorForm()
		{
			InitializeComponent();
			Icon = Resources.Icon_Vixen3;
			toolStripButton_Start.Image = Resources.control_start_blue;
			toolStripButton_Start.DisplayStyle = ToolStripItemDisplayStyle.Image;
			toolStripButton_Play.Image = Resources.control_play_blue;
			toolStripButton_Play.DisplayStyle = ToolStripItemDisplayStyle.Image;
			toolStripButton_Stop.Image = Resources.control_stop_blue;
			toolStripButton_Stop.DisplayStyle = ToolStripItemDisplayStyle.Image;
			toolStripButton_Pause.Image = Resources.control_pause_blue;
			toolStripButton_Pause.DisplayStyle = ToolStripItemDisplayStyle.Image;
			toolStripButton_End.Image = Resources.control_end_blue;
			toolStripButton_End.DisplayStyle = ToolStripItemDisplayStyle.Image;
			toolStripButton_Loop.Image = Resources.arrow_repeat;
			toolStripButton_Loop.DisplayStyle = ToolStripItemDisplayStyle.Image;
			undoButton.Image = Resources.arrow_undo;
			undoButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
			redoButton.Image = Resources.arrow_redo;
			redoButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
			redoButton.ButtonType = UndoButtonType.RedoButton;
			toolStripButton_Cut.Image = Resources.cut;
			toolStripButton_Cut.DisplayStyle = ToolStripItemDisplayStyle.Image;
			toolStripButton_Copy.Image = Resources.page_white_copy;
			toolStripButton_Copy.DisplayStyle = ToolStripItemDisplayStyle.Image;
			toolStripButton_Paste.Image = Resources.page_white_paste;
			toolStripButton_Paste.DisplayStyle = ToolStripItemDisplayStyle.Image;
			toolStripButton_AssociateAudio.Image = Resources.music;
			toolStripButton_AssociateAudio.DisplayStyle = ToolStripItemDisplayStyle.Image;
			toolStripButton_MarkManager.Image = Resources.timeline_marker;
			toolStripButton_MarkManager.DisplayStyle = ToolStripItemDisplayStyle.Image;
			toolStripButton_ZoomTimeIn.Image = Resources.zoom_in;
			toolStripButton_ZoomTimeIn.DisplayStyle = ToolStripItemDisplayStyle.Image;
			toolStripButton_ZoomTimeOut.Image = Resources.zoom_out;
			toolStripButton_ZoomTimeOut.DisplayStyle = ToolStripItemDisplayStyle.Image;
			toolStripButton_SnapTo.Image = Resources.magnet;
			toolStripButton_SnapTo.DisplayStyle = ToolStripItemDisplayStyle.Image;
			toolStripButton_DrawMode.Image = Resources.pencil;
			toolStripButton_DrawMode.DisplayStyle = ToolStripItemDisplayStyle.Image;
			toolStripButton_SelectionMode.Image = Resources.cursor_arrow;
			toolStripButton_SelectionMode.DisplayStyle = ToolStripItemDisplayStyle.Image;
			toolStripButton_DragBoxFilter.Image = Resources.table_select_big;
			toolStripButton_DragBoxFilter.DisplayStyle = ToolStripItemDisplayStyle.Image;
			toolStripButton_IncreaseTimingSpeed.Image = Resources.plus;
			toolStripButton_IncreaseTimingSpeed.DisplayStyle = ToolStripItemDisplayStyle.Image;
			toolStripButton_DecreaseTimingSpeed.Image = Resources.minus;
			toolStripButton_DecreaseTimingSpeed.DisplayStyle = ToolStripItemDisplayStyle.Image;
			toolStripSplitButton_CloseGaps.Image = Resources.fill_gaps;
			toolStripSplitButton_CloseGaps.DisplayStyle = ToolStripItemDisplayStyle.Image;

			foreach (ToolStripItem toolStripItem in toolStripDropDownButton_SnapToStrength.DropDownItems)
			{
				var toolStripMenuItem = toolStripItem as ToolStripMenuItem;
				if (toolStripMenuItem != null)
				{
					toolStripMenuItem.Click += toolStripButtonSnapToStrength_MenuItem_Click;
				}
			}

			foreach (ToolStripItem toolStripItem in toolStripSplitButton_CloseGaps.DropDownItems)
			{
				var toolStripMenuItem = toolStripItem as ToolStripMenuItem;
				if (toolStripMenuItem != null)
				{
					toolStripMenuItem.Click += toolStripButtonCloseGapStrength_MenuItem_Click;
				}
			}

			Execution.ExecutionStateChanged += OnExecutionStateChanged;
			_autoSaveTimer.Tick += AutoSaveEventProcessor;

		}

		private IDockContent DockingPanels_GetContentFromPersistString(string persistString)
		{
			if (persistString == typeof (Form_Effects).ToString())
				return EffectsForm;
			if (persistString == typeof (Form_Grid).ToString())
				return GridForm;
			if (persistString == typeof (Form_Marks).ToString())
				return MarksForm;
			if (persistString == typeof (Form_ToolPalette).ToString())
				return ToolsForm;
			
			//Else
			throw new NotImplementedException("Unable to find docking window type: " + persistString);
		}

		private void TimedSequenceEditorForm_Load(object sender, EventArgs e)
		{
			_settingsPath =
				Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vixen",
					"TimedSequenceEditorForm.xml");
			_colorCollectionsPath =
				Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vixen",
					"ColorCollections.xml");

			if (File.Exists(_settingsPath))
			{
				dockPanel.LoadFromXml(_settingsPath, DockingPanels_GetContentFromPersistString);
			}
			else
			{
				GridForm.Show(dockPanel);
				ToolsForm.Show(dockPanel, DockState.DockLeft);
				MarksForm.Show(dockPanel, DockState.DockLeft);
				EffectsForm.Show(dockPanel, DockState.DockLeft);
			}

			XMLProfileSettings xml = new XMLProfileSettings();

			//Get preferences
			_autoSaveTimer.Interval = xml.GetSetting(XMLProfileSettings.SettingType.Preferences, string.Format("{0}/AutoSaveInterval", Name), 300000);

			//Restore App Settings
			dockPanel.DockLeftPortion = xml.GetSetting(XMLProfileSettings.SettingType.AppSettings, string.Format("{0}/DockLeftPortion", Name), 150);
			dockPanel.DockRightPortion = xml.GetSetting(XMLProfileSettings.SettingType.AppSettings, string.Format("{0}/DockRightPortion", Name), 150);
			autoSaveToolStripMenuItem.Checked = xml.GetSetting(XMLProfileSettings.SettingType.AppSettings, string.Format("{0}/AutoSaveEnabled", Name), true);
			toolStripButton_SnapTo.Checked = toolStripMenuItem_SnapTo.Checked = xml.GetSetting(XMLProfileSettings.SettingType.AppSettings, string.Format("{0}/SnapToSelected", Name), true);
			PopulateSnapStrength(xml.GetSetting(XMLProfileSettings.SettingType.AppSettings, string.Format("{0}/SnapStrength", Name), 2));
			TimelineControl.grid.CloseGap_Threshold = xml.GetSetting(XMLProfileSettings.SettingType.AppSettings, string.Format("{0}/CloseGapThreshold", Name), ".100");
			toolStripMenuItem_ResizeIndicator.Checked = xml.GetSetting(XMLProfileSettings.SettingType.AppSettings, string.Format("{0}/ResizeIndicatorEnabled", Name), false);
			toolStripButton_DrawMode.Checked = TimelineControl.grid.EnableDrawMode = xml.GetSetting(XMLProfileSettings.SettingType.AppSettings, string.Format("{0}/DrawModeSelected", Name), false);
			toolStripButton_SelectionMode.Checked = xml.GetSetting(XMLProfileSettings.SettingType.AppSettings, string.Format("{0}/SelectionModeSelected", Name), true);
			ToolsForm.LinkCurves = xml.GetSetting(XMLProfileSettings.SettingType.AppSettings, string.Format("{0}/ToolPaletteLinkCurves", Name), false);
			ToolsForm.LinkGradients = xml.GetSetting(XMLProfileSettings.SettingType.AppSettings, string.Format("{0}/ToolPaletteLinkGradients", Name), false);
			cADStyleSelectionBoxToolStripMenuItem.Checked = TimelineControl.grid.aCadStyleSelectionBox = xml.GetSetting(XMLProfileSettings.SettingType.AppSettings, string.Format("{0}/CadStyleSelectionBox", Name), false);
			CheckRiColorMenuItem(xml.GetSetting(XMLProfileSettings.SettingType.AppSettings, string.Format("{0}/ResizeIndicatorColor", Name), "Red"));

			foreach (ToolStripItem toolStripItem in toolStripDropDownButton_SnapToStrength.DropDownItems)
			{
				var toolStripMenuItem = toolStripItem as ToolStripMenuItem;
				if (toolStripMenuItem != null)
				{
					if (TimelineControl.grid.SnapStrength.Equals(Convert.ToInt32(toolStripMenuItem.Tag)))
					{
						toolStripMenuItem.PerformClick();
						break;
					}
				}
			}

			foreach (ToolStripItem toolStripItem in toolStripSplitButton_CloseGaps.DropDownItems)
			{
				var toolStripMenuItem = toolStripItem as ToolStripMenuItem;
				if (toolStripMenuItem != null)
				{
					if (TimelineControl.grid.CloseGap_Threshold.Equals(toolStripMenuItem.Tag))
					{
						toolStripMenuItem.PerformClick();
						break;
					}
				}
			}

			WindowState = FormWindowState.Normal;

			if (xml.GetSetting(XMLProfileSettings.SettingType.AppSettings, string.Format("{0}/WindowState", Name),
				"Normal").Equals("Maximized"))
			{
				WindowState = FormWindowState.Maximized;
			}
			else
			{
				var desktopBounds =
					new Rectangle(
						new Point(
							xml.GetSetting(XMLProfileSettings.SettingType.AppSettings, string.Format("{0}/WindowLocationX", Name), Location.X),
							xml.GetSetting(XMLProfileSettings.SettingType.AppSettings, string.Format("{0}/WindowLocationY", Name), Location.Y)),
						new Size(
							xml.GetSetting(XMLProfileSettings.SettingType.AppSettings, string.Format("{0}/WindowWidth", Name), Size.Width),
							xml.GetSetting(XMLProfileSettings.SettingType.AppSettings, string.Format("{0}/WindowHeight", Name), Size.Height)));
				if (IsVisibleOnAnyScreen(desktopBounds))
				{
					DesktopBounds = desktopBounds;
				}
			}

			_effectNodeToElement = new Dictionary<EffectNode, Element>();
			_elementNodeToRows = new Dictionary<ElementNode, List<Row>>();

			TimelineControl.grid.RenderProgressChanged += OnRenderProgressChanged;

			TimelineControl.ElementChangedRows += ElementChangedRowsHandler;
			TimelineControl.ElementsMovedNew += timelineControl_ElementsMovedNew;
			TimelineControl.ElementDoubleClicked += ElementDoubleClickedHandler;
			TimelineControl.DataDropped += timelineControl_DataDropped;
			TimelineControl.ColorDropped += timelineControl_ColorDropped;
			TimelineControl.CurveDropped += timelineControl_CurveDropped;
			TimelineControl.GradientDropped += timelineControl_GradientDropped;

			TimelineControl.PlaybackCurrentTimeChanged += timelineControl_PlaybackCurrentTimeChanged;

			TimelineControl.RulerClicked += timelineControl_RulerClicked;
			TimelineControl.RulerBeginDragTimeRange += timelineControl_RulerBeginDragTimeRange;
			TimelineControl.RulerTimeRangeDragged += timelineControl_TimeRangeDragged;

			TimelineControl.MarkMoved += timelineControl_MarkMoved;
			TimelineControl.DeleteMark += timelineControl_DeleteMark;

			EffectsForm.EscapeDrawMode += EscapeDrawMode;

			MarksForm.MarkCollectionChecked += MarkCollection_Checked;
			MarksForm.EditMarkCollection += MarkCollection_Edit;
			MarksForm.ChangedMarkCollection += MarkCollection_Changed;

			ToolsForm.StartColorDrag += ToolPalette_ColorDrag;
			ToolsForm.StartCurveDrag += ToolPalette_CurveDrag;
			ToolsForm.StartGradientDrag += ToolPalette_GradientDrag;

			TimelineControl.SelectionChanged += TimelineControlOnSelectionChanged;
			TimelineControl.grid.MouseDown += TimelineControl_MouseDown;
			TimeLineSequenceClipboardContentsChanged += TimelineSequenceTimeLineSequenceClipboardContentsChanged;
			TimelineControl.CursorMoved += CursorMovedHandler;
			TimelineControl.ElementsSelected += timelineControl_ElementsSelected;
			TimelineControl.ContextSelected += timelineControl_ContextSelected;
			TimelineControl.SequenceLoading = false;
			TimelineControl.TimePerPixelChanged += TimelineControl_TimePerPixelChanged;
			TimelineControl.grid.SelectedElementsCloneDelegate = CloneElements;
			TimelineControl.grid.StartDrawMode += DrawElement;

			_curveLibrary = ApplicationServices.Get<IAppModuleInstance>(CurveLibraryDescriptor.ModuleID) as CurveLibrary;
			if (_curveLibrary != null)
			{
				_curveLibrary.CurveChanged += CurveLibrary_CurveChanged;
			}

			_colorGradientLibrary =
				ApplicationServices.Get<IAppModuleInstance>(ColorGradientLibraryDescriptor.ModuleID) as ColorGradientLibrary;
			if (_colorGradientLibrary != null)
			{
				_colorGradientLibrary.GradientChanged += ColorGradientLibrary_CurveChanged;
			}

			LoadAvailableEffects();
			PopulateDragBoxFilterDropDown();
			InitUndo();
			UpdateButtonStates();
			UpdatePasteMenuStates();
			LoadColorCollections();

			_library = ApplicationServices.Get<IAppModuleInstance>(LipSyncMapDescriptor.ModuleID) as LipSyncMapLibrary;

#if DEBUG
			ToolStripButton b = new ToolStripButton("[Debug Break]");
			b.Click += b_Click;
			toolStripOperations.Items.Add(b);
#endif
		}


#if DEBUG
		private void b_Click(object sender, EventArgs e)
		{
			//Debugger.Break();

			Debug.WriteLine("***** Effects in Sequence *****");
			foreach (var x in _sequence.SequenceData.EffectData)
				Debug.WriteLine("{0} - {1}: {2}", x.StartTime, x.EndTime, ((IEffectNode) x).Effect.InstanceId);
		}
#endif

		private bool IsVisibleOnAnyScreen(Rectangle rect)
		{
			return Screen.AllScreens.Any(screen => screen.WorkingArea.IntersectsWith(rect));
		}


		/// <summary>
		/// Clean up any resources being used.
		/// </summary>
		/// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
		protected override void Dispose(bool disposing)
		{
			if (_loadingTask != null && !_loadingTask.IsCompleted && !_loadingTask.IsFaulted && !_loadingTask.IsCanceled)
			{
				_cancellationTokenSource.Cancel();
			}


			//TimelineControl.grid.RenderProgressChanged -= OnRenderProgressChanged;

			TimelineControl.ElementChangedRows -= ElementChangedRowsHandler;
			TimelineControl.ElementsMovedNew -= timelineControl_ElementsMovedNew;
			TimelineControl.ElementDoubleClicked -= ElementDoubleClickedHandler;
			TimelineControl.DataDropped -= timelineControl_DataDropped;
			TimelineControl.ColorDropped -= timelineControl_ColorDropped;
			TimelineControl.CurveDropped -= timelineControl_CurveDropped;
			TimelineControl.GradientDropped -= timelineControl_GradientDropped;

			TimelineControl.PlaybackCurrentTimeChanged -= timelineControl_PlaybackCurrentTimeChanged;

			TimelineControl.RulerClicked -= timelineControl_RulerClicked;
			TimelineControl.RulerBeginDragTimeRange -= timelineControl_RulerBeginDragTimeRange;
			TimelineControl.RulerTimeRangeDragged -= timelineControl_TimeRangeDragged;
			TimelineControl.MarkMoved -= timelineControl_MarkMoved;
			TimelineControl.DeleteMark -= timelineControl_DeleteMark;

			EffectsForm.EscapeDrawMode -= EscapeDrawMode;
			EffectsForm.Dispose();

			MarksForm.EditMarkCollection -= MarkCollection_Edit;
			MarksForm.MarkCollectionChecked -= MarkCollection_Checked;
			MarksForm.ChangedMarkCollection -= MarkCollection_Changed;
			MarksForm.Dispose();

			ToolsForm.StartColorDrag -= ToolPalette_ColorDrag;
			ToolsForm.StartCurveDrag -= ToolPalette_CurveDrag;
			ToolsForm.StartGradientDrag -= ToolPalette_GradientDrag;
			ToolsForm.Dispose();

			TimelineControl.SelectionChanged -= TimelineControlOnSelectionChanged;
			TimelineControl.grid.MouseDown -= TimelineControl_MouseDown;
			TimeLineSequenceClipboardContentsChanged -= TimelineSequenceTimeLineSequenceClipboardContentsChanged;
			TimelineControl.CursorMoved -= CursorMovedHandler;
			TimelineControl.ElementsSelected -= timelineControl_ElementsSelected;
			TimelineControl.ContextSelected -= timelineControl_ContextSelected;
			TimelineControl.TimePerPixelChanged -= TimelineControl_TimePerPixelChanged;
			TimelineControl.DataDropped -= timelineControl_DataDropped;

			Execution.ExecutionStateChanged -= OnExecutionStateChanged;
			_autoSaveTimer.Stop();
			_autoSaveTimer.Tick -= AutoSaveEventProcessor;

			if (_curveLibrary != null)
			{
				_curveLibrary.CurveChanged -= CurveLibrary_CurveChanged;
			}

			if (_colorGradientLibrary != null)
			{
				_colorGradientLibrary.GradientChanged -= ColorGradientLibrary_CurveChanged;
			}

			//GRRR - make the color collections a library at some point

			foreach (ToolStripItem toolStripItem in toolStripDropDownButton_SnapToStrength.DropDownItems)
			{
				var toolStripMenuItem = toolStripItem as ToolStripMenuItem;
				if (toolStripMenuItem != null)
				{
					toolStripMenuItem.Click -= toolStripButtonSnapToStrength_MenuItem_Click;
				}
			}

			if (disposing && (components != null))
			{
				components.Dispose();
				TimelineControl.Dispose();
				GridForm.Dispose();
			}

			if (_effectNodeToElement != null)
			{
				_effectNodeToElement.Clear();
				_effectNodeToElement = null;
			}

			if (_elementNodeToRows != null)
			{
				_elementNodeToRows.Clear();
				_elementNodeToRows = null;
			}

			if (_sequence != null)
			{
				_sequence.Dispose();
				_sequence = null;
			}

			dockPanel.Dispose();

			base.Dispose(disposing);
			
		}

		private void ToolPalette_ColorDrag(object sender, EventArgs e)
		{
			TimelineControl.grid.isColorDrop = true;
		}

		private void ToolPalette_CurveDrag(object sender, EventArgs e)
		{
			TimelineControl.grid.isCurveDrop = true;
		}

		private void ToolPalette_GradientDrag(object sender, EventArgs e)
		{
			TimelineControl.grid.isGradientDrop = true;
		}

		private Form_Effects _effectsForm;

		private Form_Effects EffectsForm
		{
			get
			{
				if (_effectsForm != null && !_effectsForm.IsDisposed)
				{
					return _effectsForm;
				}
				
				_effectsForm = new Form_Effects(TimelineControl);
				return _effectsForm;
			}
		}

		private Form_Marks _marksForm;

		private Form_Marks MarksForm
		{
			get
			{
				if (_marksForm != null && !_marksForm.IsDisposed)
				{
					return _marksForm;
				}
				
				_marksForm = new Form_Marks(TimelineControl);
				return _marksForm;
			}
		}

		private Form_ToolPalette _toolPaletteForm;

		private Form_ToolPalette ToolsForm
		{
			get
			{
				if (_toolPaletteForm != null && !_toolPaletteForm.IsDisposed)
				{
					return _toolPaletteForm;
				}
				
				_toolPaletteForm = new Form_ToolPalette(TimelineControl);
				return _toolPaletteForm;
			}
		}

		private void MarkCollection_Checked(object sender, MarkCollectionArgs e)
		{
			PopulateMarkSnapTimes();
		}

		private void MarkCollection_Edit(Object sender, EventArgs e)
		{
			ShowMarkManager();
		}

		private void MarkCollection_Changed(Object sender, MarkCollectionArgs e)
		{
			SequenceModified();
		}

		private Form_Grid _gridForm;

		private Form_Grid GridForm
		{
			get { return _gridForm ?? (_gridForm = new Form_Grid()); }
		}

		private TimelineControl TimelineControl
		{
			get { return _gridForm.TimelineControl; }
		}

		private void PopulateDragBoxFilterDropDown()
		{
			ToolStripMenuItem dbfInvertMenuItem = new ToolStripMenuItem("Invert Selection")
			{
				ShortcutKeys = Keys.Control | Keys.I,
				ShowShortcutKeys = true
			};
			dbfInvertMenuItem.MouseUp += (sender, e) => toolStripDropDownButton_DragBoxFilter.ShowDropDown();
			dbfInvertMenuItem.Click += (sender, e) =>
			{
				foreach (ToolStripMenuItem mnuItem in toolStripDropDownButton_DragBoxFilter.DropDownItems)
				{
					mnuItem.Checked = (!mnuItem.Checked);
				}
			};
			toolStripDropDownButton_DragBoxFilter.DropDownItems.Add(dbfInvertMenuItem);

			foreach (IEffectModuleDescriptor effectDesriptor in
				ApplicationServices.GetModuleDescriptors<IEffectModuleInstance>().Cast<IEffectModuleDescriptor>())
			{
				//Populate Drag Box Filter drop down with effect types
				ToolStripMenuItem dbfMenuItem = new ToolStripMenuItem(effectDesriptor.EffectName,
					effectDesriptor.GetRepresentativeImage(48, 48));
				dbfMenuItem.CheckOnClick = true;
				dbfMenuItem.CheckStateChanged += (sender, e) =>
				{
					//OK, now I don't remember why I put this here, I think to make sure the list is updated when using the invert
					if (dbfMenuItem.Checked) TimelineControl.grid.DragBoxFilterTypes.Add(effectDesriptor.TypeId);
					else TimelineControl.grid.DragBoxFilterTypes.Remove(effectDesriptor.TypeId);
					//Either way...(the user is getting ready to use the filter)
					toolStripButton_DragBoxFilter.Checked = true;
				};
				dbfMenuItem.Click += (sender, e) => toolStripDropDownButton_DragBoxFilter.ShowDropDown();
				toolStripDropDownButton_DragBoxFilter.DropDownItems.Add(dbfMenuItem);
			}
		}

		private void LoadAvailableEffects()
		{
			foreach (IEffectModuleDescriptor effectDesriptor in
				ApplicationServices.GetModuleDescriptors<IEffectModuleInstance>().Cast<IEffectModuleDescriptor>())
			{
				// Add an entry to the menu
				ToolStripMenuItem menuItem = new ToolStripMenuItem(effectDesriptor.EffectName) {Tag = effectDesriptor.TypeId};
				menuItem.Click += (sender, e) =>
				{
					Row destination = TimelineControl.ActiveRow ?? TimelineControl.SelectedRow;
					if (destination != null)
					{
						AddNewEffectById((Guid) menuItem.Tag, destination, TimelineControl.CursorPosition,
							TimeSpan.FromSeconds(2)); // TODO: get a proper time
					}
				};
				addEffectToolStripMenuItem.DropDownItems.Add(menuItem);
			}
		}


		#endregion

		#region Private Properties

		private TimeSpan SequenceLength
		{
			get { return _sequence.Length; }
			set
			{
				if (_sequence.Length != value)
				{
					_sequence.Length = value;
				}

				if (TimelineControl.TotalTime != value)
				{
					TimelineControl.TotalTime = value;
				}

				toolStripStatusLabel_sequenceLength.Text = _sequence.Length.ToString("m\\:ss\\.fff");
			}
		}

		#endregion

		#region Saving / Loading Methods

		/// <summary>
		/// Loads all nodes (groups/elements) currently in the system as rows in the timeline control.
		/// </summary>
		private void LoadSystemNodesToRows(bool clearCurrentRows = true)
		{
			TimelineControl.AllowGridResize = false;
			_elementNodeToRows = new Dictionary<ElementNode, List<Row>>();

			if (clearCurrentRows)
				TimelineControl.ClearAllRows();

			TimelineControl.EnableDisableHandlers(false);
			foreach (ElementNode node in VixenSystem.Nodes.GetRootNodes())
			{
				AddNodeAsRow(node, null);
			}
			TimelineControl.EnableDisableHandlers();

			TimelineControl.LayoutRows();
			TimelineControl.ResizeGrid();
		}

		private void loadTimer_Elapsed(object sender, ElapsedEventArgs e)
		{
			UpdateToolStrip4(string.Format("Please Wait. Loading: {0}", _loadingWatch.Elapsed));
		}

		private delegate void UpdateToolStrip4Delegate(string text, int timeout = 0);

		private Timer clearStatusTimer = new Timer();

		/// <summary>
		/// Changes the text of the Status bar, with optional timeOut to clear the text after x seconds.
		/// Default timeOut is 0, the text will stay set indefinitly.
		/// </summary>
		/// <param name="text"></param>
		/// <param name="timeOut"></param>
		private void UpdateToolStrip4(string text, int timeOut = 0)
		{
			if (InvokeRequired)
			{
				Invoke(new UpdateToolStrip4Delegate(UpdateToolStrip4), text, timeOut);
			}
			else
			{
				toolStripStatusLabel4.Text = text;
				if (string.IsNullOrWhiteSpace(text))
				{
					Invalidate();
					Enabled = true;
					FormBorderStyle = FormBorderStyle.Sizable;
				}
			}

			if (timeOut > 0)
			{
				clearStatusTimer.Interval = timeOut*1000;
				clearStatusTimer.Tick += clearStatusTimer_Tick;
				clearStatusTimer.Start();
			}
		}

		private void clearStatusTimer_Tick(object sender, EventArgs e)
		{
			clearStatusTimer.Stop();
			clearStatusTimer.Tick -= clearStatusTimer_Tick;
			UpdateToolStrip4(string.Empty);
		}

		private System.Timers.Timer _loadTimer;
		private Stopwatch _loadingWatch;

		private void LoadSequence(ISequence sequence)
		{
			var taskQueue = new Queue<Task>();

			if (_loadTimer == null)
			{
				_loadTimer = new System.Timers.Timer();
				_loadTimer.Elapsed += loadTimer_Elapsed;
				_loadTimer.Interval = 250;
			}
			_loadingWatch = Stopwatch.StartNew();
			_loadTimer.Enabled = true;
			TimelineControl.SequenceLoading = true;

			// Let's get the window on the screen. Make it appear to instantly load.
			Invalidate(true);
			Update();

			try
			{
				// default the sequence to 1 minute if it's not set
				if (_sequence.Length == TimeSpan.Zero)
					_sequence.Length = DefaultSequenceTime;

				SequenceLength = _sequence.Length;
				SetTitleBarText();

				// update our program context with this sequence
				OpenSequenceContext(sequence);

				// clear out all the old data
				LoadSystemNodesToRows();

				// load the new data: get all the commands in the sequence, and make a new element for each of them.
				_effectNodeToElement = new Dictionary<EffectNode, Element>();

				TimelineControl.grid.SuppressInvalidate = true; //Hold off invalidating the grid while we bulk load.
				TimelineControl.grid.SupressRendering = true; //Hold off rendering while we load elements. 
				// This takes quite a bit of time so queue it up
				taskQueue.Enqueue(Task.Factory.StartNew(() => AddElementsForEffectNodes(_sequence.SequenceData.EffectData)));


				// Now that it is queued up, let 'er rip and start background rendering when complete.
				Task.Factory.ContinueWhenAll(taskQueue.ToArray(), completedTasks =>
				{
					// Clear the loading toolbar
					_loadingWatch.Stop();
					TimelineControl.SequenceLoading = false;
					_loadTimer.Enabled = false;
					UpdateToolStrip4(string.Empty);
					TimelineControl.grid.SupressRendering = false;
					TimelineControl.grid.SuppressInvalidate = false;
					TimelineControl.grid.RenderAllRows();
				});

				//This path is followed for new and existing sequences so we need to determine which we have and set modified accordingly.
				//Added logic to determine if the sequence has a filepath to set modified JU 8/1/2012. 
				PopulateAudioDropdown();
				_SetTimingToolStripEnabledState();

				if (String.IsNullOrEmpty(_sequence.FilePath))
				{
					SequenceModified();
				}
				else
				{
					SequenceNotModified();
				}

				MarksForm.Sequence = _sequence;
				MarksForm.PopulateMarkCollectionsList(null);
				PopulateMarkSnapTimes();

				if (_sequence.TimePerPixel > TimeSpan.Zero)
				{
					TimelineControl.TimePerPixel = _sequence.TimePerPixel;
				}



				Logging.Debug("Sequence {0} took {1} to load.", sequence.Name, _loadingWatch.Elapsed);
			}
			catch (Exception ee)
			{
				Logging.Error("TimedSequenceEditor: <LoadSequence> - Error loading sequence.", ee);
			}
		}

		/// <summary>
		/// Saves the current sequence to a file. May prompt for a file name to save the sequence to if needed.
		/// </summary>
		/// <param name="filePath">The filename to save the sequence to. If null, the filename in the sequence will be used.
		/// If that is also null, the user will be prompted for a filename.</param>
		/// <param name="forcePrompt">If true, the user will always be prompted for a filename to save the sequence to.</param>
		private void SaveSequence(string filePath = null, bool forcePrompt = false)
		{
			if (_sequence != null)
			{
				if (filePath == null | forcePrompt)
				{
					if (_sequence.FilePath.Trim() == "" || forcePrompt)
					{
						// Updated to use the OS SaveFileDialog functionality 8/1/2012 JU
						// Edit this type to be the more generic type to support importing into timed sequnces 12 FEB 2013 - JEMA
						EditorModuleDescriptorBase descriptor = ((OwnerModule.Descriptor) as EditorModuleDescriptorBase);
						saveFileDialog.InitialDirectory = SequenceService.SequenceDirectory;

						//While this should never happen, ReSharper complains about it, added logging just in case.
						if (descriptor != null)
						{
							string filter = descriptor.TypeName + " (*" + string.Join(", *", _sequence.FileExtension) + ")|*" +
							                string.Join("; *", _sequence.FileExtension);
							saveFileDialog.DefaultExt = _sequence.FileExtension;
							saveFileDialog.Filter = filter;
						}
						else
						{
							Logging.Error("TimedSequenceEditor: <SaveSequence> - Save Sequence dialog filter could not be set, EditorModuleDescriptorBase is null!");
						}

						DialogResult result = saveFileDialog.ShowDialog();
						if (result == DialogResult.OK)
						{
							string name = saveFileDialog.FileName;
							string extension = Path.GetExtension(saveFileDialog.FileName);

							// if the given extension isn't valid for this type, then keep the name intact and add an extension
							if (extension != _sequence.FileExtension)
							{
								name = name + _sequence.FileExtension;
								Logging.Info("TimedSequenceEditor: <SaveSequence> - Incorrect extension provided for timed sequence, appending one.");
							}
							_sequence.Save(name);
						}
						else
						{
							//user canceled save
							return;
						}
					}
					else
					{
						_sequence.Save();
					}
				}
				else
				{
					_sequence.Save(filePath);
				}

			}
			else
			{
				Logging.Error("TimedSequenceEditor: <SaveSequence> - Trying to save with null _sequence!");
			}
			
			SequenceNotModified();
		}

		private void SaveColorCollections()
		{
			var xmlsettings = new XmlWriterSettings
			{
				Indent = true,
				IndentChars = "\t",
			};

			DataContractSerializer dataSer = new DataContractSerializer(typeof (List<ColorCollection>));
			var dataWriter = XmlWriter.Create(_colorCollectionsPath, xmlsettings);
			dataSer.WriteObject(dataWriter, _colorCollections);
			dataWriter.Close();
		}

		private void LoadColorCollections()
		{
			if (File.Exists(_colorCollectionsPath))
			{
				using (FileStream reader = new FileStream(_colorCollectionsPath, FileMode.Open, FileAccess.Read))
				{
					DataContractSerializer ser = new DataContractSerializer(typeof (List<ColorCollection>));
					_colorCollections = (List<ColorCollection>) ser.ReadObject(reader);
				}
			}
		}

		#endregion

		#region Other Private Methods

		private void SetAutoSave()
		{
			if (autoSaveToolStripMenuItem.Checked && IsModified)
			{
				_autoSaveTimer.Start();
			}
			else
			{
				_autoSaveTimer.Stop();
			}
		}

		/// <summary>
		/// Populates the mark snaptimes in the grid.
		/// </summary>
		private void PopulateMarkSnapTimes()
		{
			TimelineControl.ClearAllSnapTimes();

			foreach (MarkCollection mc in _sequence.MarkCollections)
			{
				if (mc.Enabled)
				{
					foreach (TimeSpan time in mc.Marks)
					{
						TimelineControl.AddSnapTime(time, mc.Level, mc.MarkColor);
					}
				}
			}
		}

		private void PopulateSnapStrength(int strength)
		{
			TimelineControl.grid.SnapStrength = strength;
			TimelineControl.ruler.SnapStrength = strength;
		}

		private void PopulateAudioDropdown()
		{
			if (InvokeRequired)
			{
				Invoke(new Delegates.GenericDelegate(PopulateAudioDropdown));
			}
			else
			{
				using (var fmod = new FmodInstance())
				{
					cboAudioDevices.Items.Clear();
					fmod.AudioDevices.OrderBy(a => a.Item1).Select(b => b.Item2).ToList().ForEach(device => cboAudioDevices.Items.Add(device));
					if (cboAudioDevices.Items.Count > 0)
					{
						cboAudioDevices.SelectedIndex = 0;
						PopulateWaveformAudio();
					}
				}
			}
		}

		private delegate void ShowInvalidAudioDialogDelegate(string audioPath);

		private void ShowInvalidAudioDialog(string audioPath)
		{
			InvalidAudioPathDialog result = new InvalidAudioPathDialog(audioPath);
			result.ShowDialog(this);

			switch (result.InvalidAudioDialogResult)
			{
				case InvalidAudioDialogResult.KeepAudio:
					//Do nothing...
					break;
				case InvalidAudioDialogResult.RemoveAudio:
					RemoveAudioAssociation(false);
					break;
				case InvalidAudioDialogResult.LocateAudio:
					AddAudioAssociation(false);
					break;
			}			
		}

		private void PopulateWaveformAudio()
		{
			if (_sequence.GetAllMedia().Any())
			{
				IMediaModuleInstance media = _sequence.GetAllMedia().First();
				Audio audio = media as Audio;
				toolStripMenuItem_removeAudio.Enabled = true;
				beatBarDetectionToolStripMenuItem.Enabled = true;
				if (audio != null)
				{
					if (audio.MediaExists)
					{
						TimelineControl.Audio = audio;
						toolStripButton_AssociateAudio.ToolTipText = string.Format("Associated Audio: {0}",
							Path.GetFileName(audio.MediaFilePath));
					}
					else
					{
						//We couldn't find the audio, ask the user what to do
						//Since we are on a worker thread ...
						ShowInvalidAudioDialogDelegate dialog = ShowInvalidAudioDialog;
						BeginInvoke(dialog, audio.MediaFilePath);
					}
				}
				else
				{
					Logging.Error("TimedSequenceEditor: <PopulateWaveformAudio> - Attempting to process null audio!");
				}

			}

		}

		/// <summary>
		/// Called to update the title bar with the filename and saved / unsaved status
		/// </summary>
		private void SetTitleBarText()
		{
			if (InvokeRequired)
				Invoke(new Delegates.GenericDelegate(SetTitleBarText));
			else
			{
				//Set sequence name in title bar based on the module name and current sequence name JU 8/1/2012
				//Made this more generic to support importing 12 FEB 2013 - JEMA
				var editorModuleDescriptorBase = (OwnerModule.Descriptor) as EditorModuleDescriptorBase;
				if (editorModuleDescriptorBase != null)
				{
					Text = String.Format("{0} - [{1}{2}]", editorModuleDescriptorBase.TypeName,
						String.IsNullOrEmpty(_sequence.Name) ? "Untitled" : _sequence.Name, IsModified ? " *" : "");
				}
				else
				{
					Logging.Error("TimedSequenceEditor: <SetTitleBarText> - editorModuleDesciptorBase is null!!");
				}
			}
		}

		/// <summary>Called when the sequence is modified.</summary>
		private void SequenceModified()
		{
			if (!_mModified)
			{
				_mModified = true;
				SetTitleBarText();
				SetAutoSave();
				// TODO: Other things, like enable save button, etc.	
			}

		}

		/// <summary>Called when the sequence is no longer considered modified.</summary>
		private void SequenceNotModified()
		{
			if (_mModified)
			{
				_mModified = false;
				SetTitleBarText();
				SetAutoSave();
				// TODO: Other things, like disable save button, etc.	
			}

		}

		/// <summary>
		/// Removes the audio association from the sequence.
		/// </summary>
		/// <param name="showWarning">pass as false to prevent MessageBox warning</param>
		private void RemoveAudioAssociation(bool showWarning = true)
		{
			HashSet<IMediaModuleInstance> modulesToRemove = new HashSet<IMediaModuleInstance>();
			foreach (IMediaModuleInstance module in _sequence.GetAllMedia())
			{
				if (module is Audio)
				{
					modulesToRemove.Add(module);
				}
			}

			if (modulesToRemove.Count > 0 && showWarning)
			{
				DialogResult result =
					MessageBox.Show(@"Are you sure you want to remove the audio association?", @"Remove existing audio?", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
				if (result != DialogResult.Yes)
					return;
			}

			// we're going ahead and adding the new audio, so remove any of the old ones we found earlier
			foreach (IMediaModuleInstance module in modulesToRemove)
			{
				_sequence.RemoveMedia(module);
			}
			//Remove any associated audio from the timeline.
			TimelineControl.Audio = null;

			//Disable the menu item
			toolStripMenuItem_removeAudio.Enabled = false;
			beatBarDetectionToolStripMenuItem.Enabled = false;
			toolStripButton_AssociateAudio.ToolTipText = @"Associate Audio";

			SequenceModified();
		}

		/// <summary>
		/// Adds an audio association to the sequence
		/// </summary>
		/// <param name="showWarning">pass as false to prevent MessageBox warning when audio association already exists</param>
		private void AddAudioAssociation(bool showWarning = true)
		{
			// for now, only allow a single Audio type media to be assocated. If they want to add another, confirm and remove it.
			HashSet<IMediaModuleInstance> modulesToRemove = new HashSet<IMediaModuleInstance>();
			foreach (IMediaModuleInstance module in _sequence.GetAllMedia())
			{
				if (module is Audio)
				{
					modulesToRemove.Add(module);
				}
			}

			if (modulesToRemove.Count > 0 && showWarning)
			{
				DialogResult result =
					MessageBox.Show(@"Only one audio file can be associated with a sequence at a time. If you choose another, " +
									@"the first will be removed. Continue?", @"Remove existing audio?", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
				if (result != DialogResult.Yes)
					return;
			}

			// TODO: we need to be able to get the support file types, to filter the openFileDialog properly, but it's not
			// immediately obvious how to get that; for now, just let it open any file type and complain if it's wrong

			if (openFileDialog.ShowDialog() == DialogResult.OK)
			{
				IMediaModuleInstance newInstance = _sequence.AddMedia(openFileDialog.FileName);
				if (newInstance == null)
				{
					Logging.Warn(string.Format("Unsupported audio file {0}", openFileDialog.FileName));
					MessageBox.Show(@"The selected file is not a supported type.");
					return;
				}

				// we're going ahead and adding the new audio, so remove any of the old ones we found earlier
				foreach (IMediaModuleInstance module in modulesToRemove)
				{
					_sequence.RemoveMedia(module);
				}
				//Remove any associated audio from the timeline.
				TimelineControl.Audio = null;

				TimeSpan length = TimeSpan.Zero;
				if (newInstance is Audio)
				{
					length = (newInstance as Audio).MediaDuration;
					TimelineControl.Audio = newInstance as Audio;
				}

				_UpdateTimingSourceToSelectedMedia();

				if (length != TimeSpan.Zero)
				{
					//The only true check that needs to be done is the length of the sequence to the length of the audio
					//Perhaps this was put here to avoid the popup annoyance when creating a new sequence... Ill leave it here
					if (_sequence.Length == DefaultSequenceTime)
					{
						SequenceLength = length;
					}
					else if (_sequence.Length != length)
					{
						if (MessageBox.Show(@"Do you want to resize the sequence to the size of the audio?",
											@"Resize sequence?", MessageBoxButtons.YesNo) == DialogResult.Yes)
						{
							SequenceLength = length;
						}
					}
				}

				toolStripMenuItem_removeAudio.Enabled = true;
				beatBarDetectionToolStripMenuItem.Enabled = true;
				toolStripButton_AssociateAudio.ToolTipText = string.Format("Associated Audio: {0}", Path.GetFileName(openFileDialog.FileName));

				SequenceModified();
			}

		}

		#endregion

		#region Library Application Private Methods

		//Switch statements are the best method at this time for these methods

		private void ApplyColorCollection(ColorCollection collection, bool randomOrder)
		{
			if (!collection.Color.Any())
				return;

			bool strayElement = false;
			Color thisColor2 = Color.White;
			int iPos = 0;

			foreach (Element elem in TimelineControl.SelectedElements)
			{
				string effectName = elem.EffectNode.Effect.EffectName;
				object[] parms = new object[elem.EffectNode.Effect.ParameterValues.Count()];
				List<Color> validColors = new List<Color>();

				Array.Copy(elem.EffectNode.Effect.ParameterValues, parms, parms.Count());
				validColors.AddRange(
					elem.EffectNode.Effect.TargetNodes.SelectMany(x => ColorModule.getValidColorsForElementNode(x, true)));

				Color thisColor;
				if (randomOrder)
				{
					int r1 = rnd.Next(collection.Color.Count());
					int r2 = rnd.Next(collection.Color.Count());

					int n = 0;
					while (r1 == r2 && n <= 5)
					{
						r2 = rnd.Next(collection.Color.Count());
						n++;
					}

					thisColor = collection.Color[r1];
					thisColor2 = collection.Color[r2];
				}
				else
				{
					if (iPos == collection.Color.Count())
					{
						iPos = 0;
					}
					thisColor = collection.Color[iPos];
					iPos++;
					if (effectName == "Alternating")
					{
						thisColor2 = collection.Color[iPos];
						iPos++;
					}
				}

				if (validColors.Any() && !validColors.Contains(thisColor))
				{
					thisColor = validColors[rnd.Next(validColors.Count())];
				}

				if (effectName == "Alternate")
				{
					if (validColors.Any() && !validColors.Contains(thisColor2))
					{
						thisColor2 = validColors[rnd.Next(validColors.Count())];
					}

					int n2 = 0;
					while (thisColor2 == thisColor && n2 <= 5)
					{
						thisColor2 = validColors[rnd.Next(validColors.Count())];
						n2++;
					}
				}

				switch (effectName)
				{
					case "Candle Flicker":
					case "LipSync":
					case "Nutcracker":
					case "Launcher":
					case "RDS":
						strayElement = true;
						break;
					case "Custom Value":
						//Disabled until we fix the custom value null reference errors - not related to this.
						//parms[0] = 4; //Set it to a type of color value
						//parms[5] = thisColor;
						strayElement = true;
						break;
					case "Alternating":
						parms[1] = thisColor;
						parms[3] = thisColor2;
						parms[8] = parms[9] = true;
						break;
					case "Set Level":
						parms[1] = thisColor;
						break;
					case "Pulse":
						parms[1] = new ColorGradient(thisColor);
						break;
					case "Chase":
						parms[0] = 0; // StaticColor
						parms[3] = thisColor;
						break;
					case "Spin":
						parms[2] = 0; // StaticColor
						parms[9] = thisColor;
						break;
					case "Twinkle":
						parms[7] = 0; // StaticColor
						parms[8] = thisColor;
						break;
					case "Wipe":
						parms[0] = new ColorGradient(thisColor);
						break;
				}

				elem.EffectNode.Effect.ParameterValues = parms;
				TimelineControl.grid.RenderElement(elem);
			}
			SequenceModified();
			if (strayElement)
				MessageBox.Show(@"One or more effects were selected that do not support curves.\nAll effects that do were updated.");
		}

		#endregion

		#region Event Handlers

		private void CurveLibrary_CurveChanged(object sender, EventArgs e)
		{
			CheckAndRenderDirtyElements();
		}

		private void ColorGradientLibrary_CurveChanged(object sender, EventArgs e)
		{
			CheckAndRenderDirtyElements();
		}

		private void AutoSaveEventProcessor(object sender, EventArgs e)
		{
			_autoSaveTimer.Stop();
			if (IsModified)
			{
				Save();
			}
		}

		private void OnRenderProgressChanged(object sender, RenderElementEventArgs e)
		{
			try
			{
				if (!Disposing)
				{
					if (e.Percent >= 0 && e.Percent <= 100)
					{
						toolStripProgressBar_RenderingElements.Value = e.Percent;
					}
					if (e.Percent == 100)
					{
						toolStripProgressBar_RenderingElements.Visible = false;
						toolStripStatusLabel_RenderingElements.Visible = false;
					}
					else if (!toolStripProgressBar_RenderingElements.Visible)
					{
						toolStripProgressBar_RenderingElements.Visible = true;
						toolStripStatusLabel_RenderingElements.Visible = true;
					}
				}
			}
			catch (Exception ex)
			{
				Logging.Error("TimedSequenceEditor: <OnRenderProgressChanged> - Error updating rendering progress indicator.", ex);
			}
		}

		private void TimelineSequenceTimeLineSequenceClipboardContentsChanged(object sender, EventArgs eventArgs)
		{
			UpdatePasteMenuStates();
		}

		private void TimelineControlOnSelectionChanged(object sender, EventArgs eventArgs)
		{
			toolStripButton_Copy.Enabled = toolStripButton_Cut.Enabled = TimelineControl.SelectedElements.Any();
			toolStripMenuItem_Copy.Enabled = toolStripMenuItem_Cut.Enabled = TimelineControl.SelectedElements.Any();
		}

		private void TimelineControl_MouseDown(object sender, MouseEventArgs e)
		{
			TimelineControl.ruler.ClearSelectedMarks();
			Invalidate(true);
		}

		protected void ElementContentChangedHandler(object sender, EventArgs e)
		{
			TimedSequenceElement element = sender as TimedSequenceElement;
			TimelineControl.grid.RenderElement(element);
			SequenceModified();
		}

		protected void ElementTimeChangedHandler(object sender, EventArgs e)
		{
			//TimedSequenceElement element = sender as TimedSequenceElement;
			SequenceModified();
		}

		protected void ElementRemovedFromRowHandler(object sender, ElementEventArgs e)
		{
			// not currently used
		}

		protected void ElementAddedToRowHandler(object sender, ElementEventArgs e)
		{
			// not currently used
		}

		protected void ElementChangedRowsHandler(object sender, ElementRowChangeEventArgs e)
		{
			ElementNode oldElement = e.OldRow.Tag as ElementNode;
			ElementNode newElement = e.NewRow.Tag as ElementNode;
			TimedSequenceElement movedElement = e.Element as TimedSequenceElement;

			if (movedElement != null)
			{
				movedElement.TargetNodes = new[] {newElement};

				// now that the effect that this element has been updated to accurately reflect the change,
				// move the actual element around. It's a single element in the grid, belonging to multiple rows:
				// so find all rows that represent the old element, remove the element from them, and also find
				// all rows that represent the new element and add it to them.
				foreach (Row row in TimelineControl)
				{
					ElementNode rowElement = row.Tag as ElementNode;

					if (rowElement == oldElement && row != e.OldRow)
						row.RemoveElement(movedElement);
					if (rowElement == newElement && row != e.NewRow)
						row.AddElement(movedElement);
				}
			}

			else
			{
				Logging.Error("TimedSequenceEditor: <ElementChangedRowsHandler> - movedElement is null!");
			}

			SequenceModified();
		}

		//Sorry about this, was the only way I could find to handle the escape press if
		//the effects tree still had focus. Because... someone will do this......
		protected void EscapeDrawMode(object sender, EventArgs e)
		{
			EffectsForm.DeselectAllNodes();
			TimelineControl.grid.EnableDrawMode = false;
			toolStripButton_DrawMode.Checked = false;
			toolStripButton_SelectionMode.Checked = true;
		}

		protected void DrawElement(object sender, DrawElementEventArgs e)
		{
			//Make sure we have enough of an effect to show up
			if (e.Duration > TimeSpan.FromSeconds(.010))
			{
				var newEffects = new List<EffectNode>();
				foreach (Row drawingRow in e.Rows)
				{
					var newEffect = ApplicationServices.Get<IEffectModuleInstance>(e.Guid);


					try
					{
						newEffects.Add(CreateEffectNode(newEffect, drawingRow, e.StartTime, e.Duration));
					}
					catch (Exception ex)
					{
						string msg = "TimedSequenceEditor: <DrawElement> - error adding effect of type " +
						             newEffect.Descriptor.TypeId + " to row " +
						             ((drawingRow == null) ? "<null>" : drawingRow.Name);
							Logging.Error(msg, ex);
					}
				}
				AddEffectNodes(newEffects);
				SequenceModified();
				var act = new EffectsAddedUndoAction(this, newEffects);
				_undoMgr.AddUndoAction(act);
				SelectEffectNodes(newEffects);
			}
		}

		protected void ElementDoubleClickedHandler(object sender, ElementEventArgs e)
		{
			TimedSequenceElement element = e.Element as TimedSequenceElement;

			if (element == null || element.EffectNode == null)
			{
				Logging.Error("TimedSequenceEditor: <ElementDoubleClickedHandler> - Element doesn't have an associated effect!");
				return;
			}

			EditElement(element);
		}

		private void EditElement(TimedSequenceElement element)
		{
			EditElements(new[] {element});
		}

		private void EditElements(IEnumerable<TimedSequenceElement> elements, string elementType = null)
		{
			if (elements == null)
				return;

			IEnumerable<TimedSequenceElement> editElements;

			editElements = elementType == null
				? elements
				: elements.Where(element => element.EffectNode.Effect.EffectName == elementType);

			using (
				TimedSequenceEditorEffectEditor editor = new TimedSequenceEditorEffectEditor(editElements.Select(x => x.EffectNode))
				)
			{
				DialogResult result = editor.ShowDialog();
				if (result == DialogResult.OK)
				{
					foreach (Element element in editElements)
					{
						TimelineControl.grid.RenderElement(element);
					}
					SequenceModified();
				}
			}
		}

		private void TimelineControl_TimePerPixelChanged(object sender, EventArgs e)
		{
			if (_sequence.TimePerPixel != TimelineControl.TimePerPixel)
			{
				_sequence.TimePerPixel = TimelineControl.TimePerPixel;
				SequenceModified();
			}

		}

		private void timelineControl_ContextSelected(object sender, ContextSelectedEventArgs e)
		{

			_contextMenuStrip.Items.Clear();

			if (TimelineControl.SelectedElements.Any())
			{
				//Build Edit Menu
				ToolStripMenuItem contextMenuItemEditEffects = new ToolStripMenuItem("Edit Effect(s)");
				contextMenuItemEditEffects.ShortcutKeyDisplayString = @"Ctrl+E";

				if (TimelineControl.SelectedElementTypes.Count() > 1)
				{
					foreach (string effectType in TimelineControl.SelectedElementTypes)
					{
						ToolStripMenuItem toolStripMenuEditEffectType = new ToolStripMenuItem(effectType);
						toolStripMenuEditEffectType.Click +=
							(mySender, myE) => EditElements(TimelineControl.SelectedElements.Cast<TimedSequenceElement>(), effectType);
						contextMenuItemEditEffects.DropDownItems.Add(toolStripMenuEditEffectType);
					}

				}
				else
				{
					contextMenuItemEditEffects.Click +=
						(mySender, myE) => EditElements(TimelineControl.SelectedElements.Cast<TimedSequenceElement>());
				}

				_contextMenuStrip.Items.Add(contextMenuItemEditEffects);
			}

			ToolStripMenuItem contextMenuItemAddEffect = new ToolStripMenuItem("Add Effect(s)");

			foreach (
				IEffectModuleDescriptor effectDesriptor in
					ApplicationServices.GetModuleDescriptors<IEffectModuleInstance>().Cast<IEffectModuleDescriptor>())
			{
				// Add an entry to the menu
				ToolStripMenuItem contextMenuItemEffect = new ToolStripMenuItem(effectDesriptor.EffectName);
				contextMenuItemEffect.Tag = effectDesriptor.TypeId;
				contextMenuItemEffect.ToolTipText = @"Use Shift key to add multiple effects of the same type.";
				contextMenuItemEffect.Click += (mySender, myE) =>
				{
					if (e.Row != null)
					{
						//Modified 7-9-2014 J. Bolding - Changed so that the multiple element addition is wrapped into one action by the undo/redo engine.
						if (ModifierKeys == Keys.Shift || ModifierKeys == (Keys.Shift | Keys.Control))
						{
							//add multiple here
							AddMultipleEffects(e.GridTime, effectDesriptor.EffectName, (Guid) contextMenuItemEffect.Tag, e.Row);
						}
						else
							AddNewEffectById((Guid) contextMenuItemEffect.Tag, e.Row, e.GridTime, TimeSpan.FromSeconds(2));
					}
				};

				contextMenuItemAddEffect.DropDownItems.Add(contextMenuItemEffect);
			}

			_contextMenuStrip.Items.Add(contextMenuItemAddEffect);



			if (e.ElementsUnderCursor != null && e.ElementsUnderCursor.Count() == 1)
			{

				Element element = e.ElementsUnderCursor.FirstOrDefault();

				TimedSequenceElement tse = element as TimedSequenceElement;

				//Effect Alignment Menu
				ToolStripMenuItem contextMenuItemAlignment = new ToolStripMenuItem("Alignment")
				{
					Enabled = TimelineControl.grid.OkToUseAlignmentHelper(TimelineControl.SelectedElements)
				};
				//Disables the Alignment menu if too many effects are selected in a row.
				if (!contextMenuItemAlignment.Enabled)
				{
					contextMenuItemAlignment.ToolTipText = @"Disabled, maximum selected effects per row is 4.";
				}

				ToolStripMenuItem contextMenuItemAlignStart = new ToolStripMenuItem("Align Start Times (shift)")
				{
					ToolTipText = @"Holding shift will align the start times, while holding duration."
				};
				contextMenuItemAlignStart.Click +=
					(mySender, myE) =>
						TimelineControl.grid.AlignElementStartTimes(TimelineControl.SelectedElements, element, ModifierKeys == Keys.Shift);

				ToolStripMenuItem contextMenuItemAlignEnd = new ToolStripMenuItem("Align End Times (shift)")
				{
					ToolTipText = @"Holding shift will align the end times, while holding duration."
				};
				contextMenuItemAlignEnd.Click +=
					(mySender, myE) =>
						TimelineControl.grid.AlignElementEndTimes(TimelineControl.SelectedElements, element, ModifierKeys == Keys.Shift);

				ToolStripMenuItem contextMenuItemAlignBoth = new ToolStripMenuItem("Align Both Times");
				contextMenuItemAlignBoth.Click +=
					(mySender, myE) => TimelineControl.grid.AlignElementStartEndTimes(TimelineControl.SelectedElements, element);

				ToolStripMenuItem contextMenuItemMatchDuration = new ToolStripMenuItem("Match Duration (shift)")
				{
					ToolTipText =
						@"Holding shift will hold the effects end time and adjust the start time, by default the end time is adjusted."
				};
				contextMenuItemMatchDuration.Click +=
					(mySender, myE) =>
						TimelineControl.grid.AlignElementDurations(TimelineControl.SelectedElements, element, ModifierKeys == Keys.Shift);

				ToolStripMenuItem contextMenuItemAlignStartToEnd = new ToolStripMenuItem("Align Start to End (shift)")
				{
					ToolTipText =
						@"Holding shift will hold the effects end time and only adjust the start time, by default the entire effect is moved."
				};
				contextMenuItemAlignStartToEnd.Click +=
					(mySender, myE) =>
						TimelineControl.grid.AlignElementStartToEndTimes(TimelineControl.SelectedElements, element,
							ModifierKeys == Keys.Shift);

				ToolStripMenuItem contextMenuItemAlignEndToStart = new ToolStripMenuItem("Align End to Start (shift)")
				{
					ToolTipText =
						@"Holding shift will hold the effects start time and only adjust the end time, by default the entire effect is moved."
				};
				contextMenuItemAlignEndToStart.Click +=
					(mySender, myE) =>
						TimelineControl.grid.AlignElementEndToStartTime(TimelineControl.SelectedElements, element,
							ModifierKeys == Keys.Shift);

				ToolStripMenuItem contextMenuItemDistDialog = new ToolStripMenuItem("Distribute Effects");
				contextMenuItemDistDialog.Click += (mySender, myE) => DistributeSelectedEffects();

				ToolStripMenuItem contextMenuItemAlignCenter = new ToolStripMenuItem("Align Centerpoints");
				contextMenuItemAlignCenter.Click +=
					(mySender, myE) => TimelineControl.grid.AlignElementCenters(TimelineControl.SelectedElements, element);

				ToolStripMenuItem contextMenuItemDistributeEqually = new ToolStripMenuItem("Distribute Equally")
				{
					ToolTipText =
						@"This will stair step the selected elements, starting with the element that has the earlier start point on the time line."
				};
				contextMenuItemDistributeEqually.Click += (mySender, myE) => DistributeSelectedEffectsEqually();

				ToolStripMenuItem contextMenuItemAlignStartToMark = new ToolStripMenuItem("Align Start to nearest mark");
				contextMenuItemAlignStartToMark.Click += (mySender, myE) => AlignEffectsToNearestMarks(true);

				ToolStripMenuItem contextMenuItemAlignEndToMark = new ToolStripMenuItem("Align End to nearest mark");
				contextMenuItemAlignEndToMark.Click += (mySender, myE) => AlignEffectsToNearestMarks(false);

				_contextMenuStrip.Items.Add(contextMenuItemAlignment);
				contextMenuItemAlignment.DropDown.Items.Add(contextMenuItemAlignStart);
				contextMenuItemAlignment.DropDown.Items.Add(contextMenuItemAlignEnd);
				contextMenuItemAlignment.DropDown.Items.Add(contextMenuItemAlignBoth);
				contextMenuItemAlignment.DropDown.Items.Add(contextMenuItemAlignCenter);
				contextMenuItemAlignment.DropDown.Items.Add(contextMenuItemMatchDuration);
				contextMenuItemAlignment.DropDown.Items.Add(contextMenuItemAlignStartToEnd);
				contextMenuItemAlignment.DropDown.Items.Add(contextMenuItemAlignEndToStart);
				contextMenuItemAlignment.DropDown.Items.Add(contextMenuItemDistributeEqually);
				contextMenuItemAlignment.DropDown.Items.Add(contextMenuItemDistDialog);
				contextMenuItemAlignment.DropDown.Items.Add(contextMenuItemAlignStartToMark);
				contextMenuItemAlignment.DropDown.Items.Add(contextMenuItemAlignEndToMark);

				if (tse != null)
				{
					//Effect Manipulation Menu
					ToolStripMenuItem contextMenuItemManipulation = new ToolStripMenuItem("Manipulation");
					ToolStripMenuItem contextMenuItemManipulateDivide = new ToolStripMenuItem("Divide at cursor");
					contextMenuItemManipulateDivide.Click += (mySender, myE) =>
					{
						if (TimelineControl.SelectedElements.Any())
						{
							TimelineControl.grid.SplitElementsAtTime(
								TimelineControl.SelectedElements.Where(elem => elem.StartTime < e.GridTime && elem.EndTime > e.GridTime)
									.ToList(), e.GridTime);
						}
						else
						{
							TimelineControl.grid.SplitElementsAtTime(new List<Element> {element}, e.GridTime);
						}

					};

					ToolStripMenuItem contextMenuItemManipulationClone = new ToolStripMenuItem("Clone");
					contextMenuItemManipulationClone.Click += (mySender, myE) =>
					{
						if (TimelineControl.SelectedElements.Any())
						{
							CloneElements(TimelineControl.SelectedElements ?? new List<Element> {element});
						}
						else
						{
							CloneElements(new List<Element> {element});
						}
					};

					ToolStripMenuItem contextMenuItemManipulationCloneToOther = new ToolStripMenuItem("Clone to selected effects");
					contextMenuItemManipulationCloneToOther.Click += (mySender, myE) =>
					{
						if (TimelineControl.SelectedElements.Any(elem => elem.EffectNode.Effect.TypeId != element.EffectNode.Effect.TypeId))
						{
							var dr = MessageBox.Show(
								string.Format(
									"Some of the selected effects are not of the same type, only effects of {0} type will be modified.",
									element.EffectNode.Effect.EffectName), @"Multiple type effect selected", MessageBoxButtons.OKCancel,
								MessageBoxIcon.Warning);
							if (dr == DialogResult.Cancel) return;
						}

						foreach (
							Element elem in
								TimelineControl.SelectedElements.Where(elem => elem != element)
									.Where(elem => elem.EffectNode.Effect.TypeId == element.EffectNode.Effect.TypeId))
						{
							elem.EffectNode.Effect.ParameterValues = element.EffectNode.Effect.ParameterValues;
							elem.RenderElement();
						}
					};
					contextMenuItemManipulationCloneToOther.Enabled = (TimelineControl.SelectedElements.Count() > 2);

					_contextMenuStrip.Items.Add(contextMenuItemManipulation);
					contextMenuItemManipulation.DropDown.Items.Add(contextMenuItemManipulateDivide);
					contextMenuItemManipulation.DropDown.Items.Add(contextMenuItemManipulationClone);
					contextMenuItemManipulation.DropDown.Items.Add(contextMenuItemManipulationCloneToOther);

					ToolStripMenuItem contextMenuItemEditTime = new ToolStripMenuItem("Edit Time");
					contextMenuItemEditTime.Click += (mySender, myE) =>
					{
						EffectTimeEditor editor = new EffectTimeEditor(tse.EffectNode.StartTime, tse.EffectNode.TimeSpan);
						if (editor.ShowDialog(this) != DialogResult.OK) return;

						if (TimelineControl.SelectedElements.Any())
						{
							var elementsToMove = TimelineControl.SelectedElements.ToDictionary(elem => elem,
								elem => new Tuple<TimeSpan, TimeSpan>(editor.Start, editor.Start + editor.Duration));
							TimelineControl.grid.MoveResizeElements(elementsToMove);
						}
						else
						{
							TimelineControl.grid.MoveResizeElement(element, editor.Start, editor.Duration);
						}
					};
					//Why do we set .Tag ?
					contextMenuItemEditTime.Tag = tse;
					contextMenuItemEditTime.Enabled = TimelineControl.grid.OkToUseAlignmentHelper(TimelineControl.SelectedElements);
					if (!contextMenuItemEditTime.Enabled)
						contextMenuItemEditTime.ToolTipText = @"Disabled, maximum selected effects per row is 4.";
					_contextMenuStrip.Items.Add(contextMenuItemEditTime);

				}
			}

			//Add Copy/Cut/paste section
			//Previously this section used the toolstripmenuitems from the main menu bar, however this caused those items
			//to be deleted from the edit menu. This is the work-around for that issue - JMB 12-14-2014
			_contextMenuStrip.Items.Add("-");

			ToolStripMenuItem contextMenuItemCopy = new ToolStripMenuItem("Copy", null, toolStripMenuItem_Copy_Click)
			{
				ShortcutKeyDisplayString = @"Ctrl+C"
			};
			ToolStripMenuItem contextMenuItemCut = new ToolStripMenuItem("Cut", null, toolStripMenuItem_Cut_Click)
			{
				ShortcutKeyDisplayString = @"Ctrl+X"
			};
			contextMenuItemCopy.Enabled = contextMenuItemCut.Enabled = TimelineControl.SelectedElements.Any();
			ToolStripMenuItem contextMenuItemPaste = new ToolStripMenuItem("Paste", null, toolStripMenuItem_Paste_Click)
			{
				ShortcutKeyDisplayString = @"Ctrl+V",
				Enabled = ClipboardHasData()
			};

			_contextMenuStrip.Items.AddRange(new ToolStripItem[] {contextMenuItemCopy, contextMenuItemCut, contextMenuItemPaste});

			if (TimelineControl.SelectedElements.Any())
			{
				//Add Delete/Collections
				ToolStripMenuItem contextMenuItemDelete = new ToolStripMenuItem("Delete Effect(s)", null,
					toolStripMenuItem_deleteElements_Click) {ShortcutKeyDisplayString = @"Del"};
				_contextMenuStrip.Items.Add(contextMenuItemDelete);
				AddContextCollectionsMenu();

			}

			e.AutomaticallyHandleSelection = false;

			_contextMenuStrip.Show(MousePosition);
		}

		private void AddContextCollectionsMenu()
		{
			ToolStripMenuItem contextMenuItemCollections = new ToolStripMenuItem("Collections");

			if (TimelineControl.SelectedElements.Count() > 1 && _colorCollections.Any())
			{
				ToolStripMenuItem contextMenuItemColorCollections = new ToolStripMenuItem("Colors");
				ToolStripMenuItem contextMenuItemRandomColors = new ToolStripMenuItem("Random");
				ToolStripMenuItem contextMenuItemSequentialColors = new ToolStripMenuItem("Sequential");

				contextMenuItemCollections.DropDown.Items.Add(contextMenuItemColorCollections);
				contextMenuItemColorCollections.DropDown.Items.Add(contextMenuItemRandomColors);
				contextMenuItemColorCollections.DropDown.Items.Add(contextMenuItemSequentialColors);

				foreach (ColorCollection collection in _colorCollections)
				{
					if (collection.Color.Any())
					{
						ToolStripMenuItem contextMenuItemRandomColorItem = new ToolStripMenuItem(collection.Name);
						contextMenuItemRandomColorItem.ToolTipText = collection.Description;
						contextMenuItemRandomColorItem.Click += (mySender, myE) => ApplyColorCollection(collection, true);
						contextMenuItemRandomColors.DropDown.Items.Add(contextMenuItemRandomColorItem);

						ToolStripMenuItem contextMenuItemSequentialColorItem = new ToolStripMenuItem(collection.Name);
						contextMenuItemSequentialColorItem.ToolTipText = collection.Description;
						contextMenuItemSequentialColorItem.Click += (mySender, myE) => ApplyColorCollection(collection, false);
						contextMenuItemSequentialColors.DropDown.Items.Add(contextMenuItemSequentialColorItem);	
					}
				}

				if (contextMenuItemCollections.DropDownItems.Count > 0)
				{
					_contextMenuStrip.Items.Add("-");
					_contextMenuStrip.Items.Add(contextMenuItemCollections);
				}
			}
		}

		private void toolStripButton_DrawMode_Click(object sender, EventArgs e)
		{
			TimelineControl.grid.EnableDrawMode = true;
			toolStripButton_DrawMode.Checked = true;
			toolStripButton_SelectionMode.Checked = false;
		}

		private void toolStripButton_SelectionMode_Click(object sender, EventArgs e)
		{
			TimelineControl.grid.EnableDrawMode = false;
			toolStripButton_SelectionMode.Checked = true;
			toolStripButton_DrawMode.Checked = false;
		}


		private void toolStripMenuItem_ResizeIndicator_CheckStateChanged(object sender, EventArgs e)
		{
			TimelineControl.grid.ResizeIndicator_Enabled = toolStripMenuItem_ResizeIndicator.Checked;
		}


		private void CheckRiColorMenuItem(string color)
		{
			TimelineControl.grid.ResizeIndicator_Color = color;
			toolStripMenuItem_RIColor_Blue.Checked = color == "Blue";
			toolStripMenuItem_RIColor_Yellow.Checked = color == "Yellow";
			toolStripMenuItem_RIColor_Green.Checked = color == "Green";
			toolStripMenuItem_RIColor_White.Checked = color == "White";
			toolStripMenuItem_RIColor_Red.Checked = color == "Red";
		}
		private void toolStripMenuItem_RIColor_Blue_Click(object sender, EventArgs e)
		{
			CheckRiColorMenuItem("Blue");
			toolStripMenuItem_ResizeIndicator.Checked = true;
		}

		private void toolStripMenuItem_RIColor_Yellow_Click(object sender, EventArgs e)
		{
			CheckRiColorMenuItem("Yellow");
			toolStripMenuItem_ResizeIndicator.Checked = true;
		}

		private void toolStripMenuItem_RIColor_Green_Click(object sender, EventArgs e)
		{
			CheckRiColorMenuItem("Green");
			toolStripMenuItem_ResizeIndicator.Checked = true;
		}

		private void toolStripMenuItem_RIColor_White_Click(object sender, EventArgs e)
		{
			CheckRiColorMenuItem("White");
			toolStripMenuItem_ResizeIndicator.Checked = true;
		}

		private void toolStripMenuItem_RIColor_Red_Click(object sender, EventArgs e)
		{
			CheckRiColorMenuItem("Red");
			toolStripMenuItem_ResizeIndicator.Checked = true;
		}

		private void toolStripButton_DragBoxFilter_CheckedChanged(object sender, EventArgs e)
		{
			TimelineControl.grid.DragBoxFilterEnabled = toolStripButton_DragBoxFilter.Checked;
		}

		private void ColorCollectionsToolStripMenuItem_Click(object sender, EventArgs e)
		{
			ColorCollectionLibrary_Form rccf = new ColorCollectionLibrary_Form(new List<ColorCollection>(_colorCollections));
			if (rccf.ShowDialog() == DialogResult.OK)
			{
				_colorCollections = rccf.ColorCollections;
				SaveColorCollections();
			}
			else
			{
				LoadColorCollections();
			}
		}

		private void AddMultipleEffects(TimeSpan startTime, String effectName, Guid effectId, Row row)
		{
			var eDialog = new Form_AddMultipleEffects();
			if (ModifierKeys == (Keys.Shift | Keys.Control) && _amLastEffectCount > 0)
			{
				eDialog.EffectCount = _amLastEffectCount;
				eDialog.StartTime = _amLastStartTime;
				eDialog.Duration = _amLastDuration;
				eDialog.DurationBetween = _amLastDurationBetween;
			}
			else
			{
				eDialog.EffectCount = 2;
				eDialog.StartTime = startTime;
				eDialog.Duration = TimeSpan.FromSeconds(2);
				eDialog.DurationBetween = TimeSpan.FromSeconds(2);
			}
			eDialog.EffectName = effectName;
			eDialog.SequenceLength = eDialog.EndTime = SequenceLength;
			eDialog.MarkCollections = _sequence.MarkCollections;
			eDialog.ShowDialog();

			if (eDialog.DialogResult == DialogResult.OK)
			{
				_amLastEffectCount = eDialog.EffectCount;
				_amLastStartTime = eDialog.StartTime;
				_amLastDuration = eDialog.Duration;
				_amLastDurationBetween = eDialog.DurationBetween;

				var newEffects = new List<EffectNode>();

				if (eDialog.AlignToBeatMarks)
				{
					newEffects = AddEffectsToBeatMarks(eDialog.CheckedMarks, eDialog.EffectCount, effectId, eDialog.StartTime, eDialog.Duration, row, eDialog.FillDuration, eDialog.SkipEOBeat);
				}
				else
				{
					TimeSpan nextStartTime = eDialog.StartTime;
					for (int i = 0; i < eDialog.EffectCount; i++)
					{
						if (nextStartTime + eDialog.Duration > SequenceLength)
						{
							//if something went wrong in the forms calculations
							break;
						}
						
						var newEffect = ApplicationServices.Get<IEffectModuleInstance>(effectId);
						try
						{
							newEffects.Add(CreateEffectNode(newEffect, row, nextStartTime, eDialog.Duration));
							nextStartTime = nextStartTime + eDialog.Duration + eDialog.DurationBetween;
						}
						catch (Exception ex)
						{
							string msg = "TimedSequenceEditor: <AddMultipleEffects> - error adding effect of type " + newEffect.Descriptor.TypeId + " to row " +
							             ((row == null) ? "<null>" : row.Name);
								Logging.Error(msg, ex);
						}
					}
					AddEffectNodes(newEffects);
					SequenceModified();
					var act = new EffectsAddedUndoAction(this, newEffects);
					_undoMgr.AddUndoAction(act);
				}
				if (newEffects.Count > 0)
				{
					if (eDialog.SelectEffects || eDialog.EditEffects) SelectEffectNodes(newEffects);
					if (eDialog.EditEffects && TimelineControl.SelectedElements.Any())
					{
						EditElements(TimelineControl.SelectedElements.Cast<TimedSequenceElement>());
					}

				}
			}
		}
		private List<EffectNode> AddEffectsToBeatMarks(ListView.CheckedListViewItemCollection checkedMarks, int effectCount, Guid effectGuid, TimeSpan startTime, TimeSpan duration, Row row, Boolean fillDuration, Boolean skipEoBeat)
		{
			bool skipThisBeat = false;

			List<TimeSpan> times = (from ListViewItem listItem in checkedMarks from mcItem in _sequence.MarkCollections where mcItem.Name == listItem.Text from mark in mcItem.Marks where mark >= startTime select mark).ToList();

			times.Sort();

			var newEffects = new List<EffectNode>();

			if (times.Count > 0)
			{
				foreach (TimeSpan mark in times)
				{
					if (newEffects.Count < effectCount)
					{
						if (!skipThisBeat)
						{
							var newEffect = ApplicationServices.Get<IEffectModuleInstance>(effectGuid);
							try
							{
								if (fillDuration)
								{
									if (times.IndexOf(mark) == times.Count - 1) //The dialog hanles this, but just to make sure
										break; //We're done -- There are no more marks to fill, don't create it
									duration = times[times.IndexOf(mark) + 1] - mark;
									if (duration < TimeSpan.FromSeconds(.01)) duration = TimeSpan.FromSeconds(.01);
								}
								newEffects.Add(CreateEffectNode(newEffect, row, mark, duration));
							}
							catch (Exception ex)
							{
								string msg = "TimedSequenceEditor: <AddEffectsToBeatMarks> - error adding effect of type " + newEffect.Descriptor.TypeId + " to row " +
											 ((row == null) ? "<null>" : row.Name);
								Logging.Error(msg, ex);
							}
						}
						
						if (skipEoBeat) skipThisBeat = (!skipThisBeat);
					}
					else
						break; //We're done creating, we've matched counts
				}

				AddEffectNodes(newEffects);
				SequenceModified();
				var act = new EffectsAddedUndoAction(this, newEffects);
				_undoMgr.AddUndoAction(act);
			}

			return newEffects;
		}

		private void DistributeSelectedEffectsEqually()
		{
			if (!TimelineControl.grid.OkToUseAlignmentHelper(TimelineControl.SelectedElements))
			{
				MessageBox.Show(TimelineControl.grid.alignmentHelperWarning);
				return;
			}

			//Before we do anything lets make sure there is time to work with
			//I don't remember why I put this here, for now its commented out until its verified that its not needed, then it will be removed
			//if (TimelineControl.SelectedElements.First().EndTime == TimelineControl.SelectedElements.Last().EndTime)
			//{
			//	MessageBox.Show("The first and last effect cannot have the same end time.", "Warning", MessageBoxButtons.OK);
			//	return;
			//}
			bool startAtLastElement = false;
			var totalElements = TimelineControl.SelectedElements.Count();
			var startTime = TimelineControl.SelectedElements.First().StartTime;
			var endTime = TimelineControl.SelectedElements.Last().EndTime;
			if (TimelineControl.SelectedElements.First().StartTime > TimelineControl.SelectedElements.Last().StartTime)
			{
				startAtLastElement = true;
				startTime = TimelineControl.SelectedElements.Last().StartTime;
				endTime = TimelineControl.SelectedElements.First().EndTime;
			}
			var totalDuration = endTime - startTime;
			var effectDuration = totalDuration.TotalSeconds/totalElements;
			TimeSpan effectTs = TimeSpan.FromSeconds(effectDuration);
			//var msgString = string.Format("Total Elements: {0}\n Start Time: {1}\n End Time: {2}\n Total Duration: {3}\n Effect Duration: {4}\n TimeSpan Duration: {5}\n Start at last element: {6}", totalElements,startTime,endTime,totalDuration,effectDuration, effectTS.TotalSeconds, startAtLastElement);
			//MessageBox.Show(msgString);
			//Sanity Check - Keep effects from becoming less than minimum.
			if (effectDuration < .050)
			{
				MessageBox.Show(
					string.Format(
						"Unable to complete request. The resulting duration would fall below 50 milliseconds.\nCalculated duration: {0}",
						effectDuration), @"Warning", MessageBoxButtons.OK);
				return;
			}

			var elementsToDistribute = new Dictionary<Element, Tuple<TimeSpan, TimeSpan>>();
			if (!startAtLastElement)
			{
				//Lets move the first one
				elementsToDistribute.Add(TimelineControl.SelectedElements.ElementAt(0),
							new Tuple<TimeSpan, TimeSpan>(startTime, startTime + effectTs));
				for (int i = 1; i <= totalElements - 1; i++)
				{
					var thisStartTime = elementsToDistribute.Last().Value.Item2;
					elementsToDistribute.Add(TimelineControl.SelectedElements.ElementAt(i), new Tuple<TimeSpan, TimeSpan>(thisStartTime, thisStartTime + effectTs));
				}
			}
			else
			{
				//Lets move the first(last) one
				elementsToDistribute.Add(TimelineControl.SelectedElements.Last(), new Tuple<TimeSpan, TimeSpan>(startTime, startTime + effectTs));
				for (int i = totalElements - 2; i >= 0; i--)
				{
					var thisStartTime = elementsToDistribute.Last().Value.Item2; 
					elementsToDistribute.Add(TimelineControl.SelectedElements.ElementAt(i), new Tuple<TimeSpan, TimeSpan>(thisStartTime, thisStartTime + effectTs));
				}
			}

			if (elementsToDistribute.Any())
			{
				TimelineControl.grid.MoveResizeElements(elementsToDistribute, ElementMoveType.Distribute);
			}
		}

		private void DistributeSelectedEffects()
		{
			if (!TimelineControl.grid.OkToUseAlignmentHelper(TimelineControl.SelectedElements))
			{
				MessageBox.Show(TimelineControl.grid.alignmentHelperWarning);
				return;
			}

			var startTime = TimelineControl.SelectedElements.First().StartTime;
			var endTime = TimelineControl.SelectedElements.Last().EndTime;
			if (startTime > endTime)
			{
				startTime = TimelineControl.SelectedElements.Last().StartTime;
				endTime = TimelineControl.SelectedElements.First().EndTime;
			}
			var dDialog = new EffectDistributionDialog();
			var elementCount = TimelineControl.SelectedElements.Count();

			dDialog.ElementCount = elementCount.ToString(CultureInfo.InvariantCulture);
			dDialog.StartTime = startTime;
			dDialog.EndTime = endTime;
			dDialog.RadioEqualDuration = true;
			dDialog.RadioStairStep = true;
			dDialog.StartWithFirst = true;
			dDialog.ShowDialog();
			if (dDialog.DialogResult == DialogResult.OK)
			{
				startTime = dDialog.StartTime;
				endTime = dDialog.EndTime;
				TimeSpan duration = endTime - startTime;
				double offset = duration.TotalSeconds/elementCount;

				var elementsToDistribute = new Dictionary<Element, Tuple<TimeSpan, TimeSpan>>();
				if (dDialog.StartWithFirst)
				{
					//We start with the first effect
					for (int i = 0; i <= elementCount - 1; i++)
					{
						double thisStartTime = startTime.TotalSeconds;
						double thisEndTime = thisStartTime + offset;
						//Generic placement of starttime eq to prev end time
						if (i > 0)
							thisStartTime = elementsToDistribute.Last().Value.Item2.TotalSeconds;
						//Determine Start time
						if (i > 0 && dDialog.RadioEffectPlacementOverlap)
							thisStartTime = thisStartTime - Convert.ToDouble(dDialog.EffectPlacementOverlap.TotalSeconds);
						if (i > 0 && dDialog.RadioPlacementSpacedDuration)
							thisStartTime = thisStartTime + Convert.ToDouble(dDialog.SpacedPlacementDuration.TotalSeconds);
						if (dDialog.RadioDoNotChangeDuration && !dDialog.RadioEffectPlacementOverlap &&
						    !dDialog.RadioPlacementSpacedDuration)
							thisStartTime = startTime.TotalSeconds + (offset*i);
						//Determine End time
						if (dDialog.RadioEqualDuration)
							thisEndTime = thisStartTime + offset;
						if (dDialog.RadioDoNotChangeDuration)
							thisEndTime = thisStartTime + TimelineControl.SelectedElements.ElementAt(i).Duration.TotalSeconds;
						if (dDialog.RadioSpecifiedDuration)
							thisEndTime = thisStartTime + Convert.ToDouble(dDialog.SpecifiedEffectDuration.TotalSeconds);
						elementsToDistribute.Add(TimelineControl.SelectedElements.ElementAt(i),
							new Tuple<TimeSpan, TimeSpan>(TimeSpan.FromSeconds(thisStartTime), TimeSpan.FromSeconds(thisEndTime)));
					}
				}
				if (dDialog.StartWithLast)
				{
					//We start with the last effect
					int placeCount = 0;
					for (int i = elementCount - 1; i >= 0; i--)
					{
						var thisStartTime = startTime.TotalSeconds;
						var thisEndTime = thisStartTime + offset;
						//Generic placement of starttime eq to prev end time
						if (i < elementCount - 1)
							thisStartTime = elementsToDistribute.Last().Value.Item2.TotalSeconds;
						//Determine Start time
						if (i < elementCount - 1 && dDialog.RadioEffectPlacementOverlap)
							thisStartTime = thisStartTime - Convert.ToDouble(dDialog.EffectPlacementOverlap.TotalSeconds);
						if (i < elementCount - 1 && dDialog.RadioPlacementSpacedDuration)
							thisStartTime = thisStartTime + Convert.ToDouble(dDialog.SpacedPlacementDuration.TotalSeconds);
						if (dDialog.RadioDoNotChangeDuration && !dDialog.RadioEffectPlacementOverlap &&
						    !dDialog.RadioPlacementSpacedDuration)
							thisStartTime = startTime.TotalSeconds + (offset*placeCount);
						//Determine End time
						if (dDialog.RadioEqualDuration)
							thisEndTime = thisStartTime + offset;
						if (dDialog.RadioDoNotChangeDuration)
							thisEndTime = thisStartTime + TimelineControl.SelectedElements.ElementAt(i).Duration.TotalSeconds;
						if (dDialog.RadioSpecifiedDuration)
							thisEndTime = thisStartTime + Convert.ToDouble(dDialog.SpecifiedEffectDuration.TotalSeconds);
						elementsToDistribute.Add(TimelineControl.SelectedElements.ElementAt(i),
							new Tuple<TimeSpan, TimeSpan>(TimeSpan.FromSeconds(thisStartTime), TimeSpan.FromSeconds(thisEndTime)));
						placeCount++;
					}
				}
				if (elementsToDistribute.Any())
				{
					TimelineControl.grid.MoveResizeElements(elementsToDistribute, ElementMoveType.Distribute);
				}
			}
		}

		private void timelineControl_ElementsSelected(object sender, ElementsSelectedEventArgs e)
		{
			if (e.ElementsUnderCursor != null && e.ElementsUnderCursor.Count() > 1)
			{
				contextMenuStripElementSelection.Items.Clear();

				foreach (Element element in e.ElementsUnderCursor)
				{
					TimedSequenceElement tse = element as TimedSequenceElement;
					if (tse == null)
						continue;

					string name = tse.EffectNode.Effect.Descriptor.TypeName;
					name += string.Format(" ({0:m\\:ss\\.fff})", tse.EffectNode.StartTime);
					ToolStripMenuItem item = new ToolStripMenuItem(name);
					item.Click += contextMenuStripElementSelectionItem_Click;
					item.Tag = tse;
					contextMenuStripElementSelection.Items.Add(item);
				}

				e.AutomaticallyHandleSelection = false;

				contextMenuStripElementSelection.Show(MousePosition);
			}
		}

		private void contextMenuStripElementSelectionItem_Click(object sender, EventArgs e)
		{
			var toolStripMenuItem = sender as ToolStripMenuItem;
			if (toolStripMenuItem != null)
			{
				TimedSequenceElement tse = toolStripMenuItem.Tag as TimedSequenceElement;
				if (tse != null)
					TimelineControl.SelectElement(tse);
			}
			else
			{
				Logging.Error("TimedSequenceEditor: <contextMenuStripElementSelectionItem_Click> - toolStripMenuItem is null!");
			}
		}

		private void timelineControl_RulerClicked(object sender, RulerClickedEventArgs e)
		{
			if (_context == null)
			{
				Logging.Error("TimedSequenceEditor: <timelineControl_RulerCLicked> - StartPointClicked with null context!");
				return;
			}

			if (e.Button == MouseButtons.Left)
			{
				bool autoPlay = e.ModifierKeys.HasFlag(Keys.Control);

				if (autoPlay)
				{
					// Save the times for later restoration
					_mPrevPlaybackStart = TimelineControl.PlaybackStartTime;
					_mPrevPlaybackEnd = TimelineControl.PlaybackEndTime;
				}
				else
				{
					_mPrevPlaybackStart = e.Time;
					_mPrevPlaybackEnd = null;
				}

				// Set the timeline control
				TimelineControl.PlaybackStartTime = e.Time;
				TimelineControl.PlaybackEndTime = null;

				if (autoPlay)
				{
					_PlaySequence(e.Time, TimeSpan.MaxValue);
				}
				else
				{
					TimelineControl.CursorPosition = e.Time;
				}
			}
			else if (e.Button == MouseButtons.Right)
			{
				MarkCollection mc = null;
				if (_sequence.MarkCollections.Count == 0)
				{
					if (MessageBox.Show(@"Marks are stored in Mark Collections. There are no mark collections available to store this mark. Would you like to create a new one?", @"Creat a Mark Collection", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
					{
						mc = GetOrAddNewMarkCollection(Color.White, "Default Marks");
						MarksForm.PopulateMarkCollectionsList(mc);
					}
				}
				else
				{
					mc = MarksForm.SelectedMarkCollection;
					if (mc == null)
					{
						MessageBox.Show(@"Please select a mark collection in the Mark Manager window before adding a new mark to the timeline.", @"New Mark", MessageBoxButtons.OKCancel, MessageBoxIcon.Error);
					}
				}
				if (mc != null)
				{
					mc.Marks.Add(e.Time);
					PopulateMarkSnapTimes();
					SequenceModified();
				}
			}
		}

		private MarkCollection GetOrAddNewMarkCollection(Color color, string name = "New Collection")
		{
			MarkCollection mc = _sequence.MarkCollections.FirstOrDefault(mCollection => mCollection.Name == name);
			if (mc == null)
			{
				MarkCollection newCollection = new MarkCollection {Name = name, MarkColor = color};
				_sequence.MarkCollections.Add(newCollection);
				mc = newCollection;
				SequenceModified();
			}

			return mc;
		}

		private void timelineControl_MarkMoved(object sender, MarkMovedEventArgs e)
		{
			foreach (MarkCollection mc in _sequence.MarkCollections)
			{
				if (/*e.SnapDetails.SnapColor == mc.MarkColor && */e.SnapDetails.SnapLevel == mc.Level)
				{
					if (mc.Marks.Contains(e.OriginalMark))
					{
						mc.Marks.Remove(e.OriginalMark);
						mc.Marks.Add(e.NewMark);
					}
				}
			}
			PopulateMarkSnapTimes();
			SequenceModified();
		}

		private void timelineControl_DeleteMark(object sender, DeleteMarkEventArgs e)
		{
			foreach (MarkCollection mc in _sequence.MarkCollections)
			{
				if (mc.Marks.Contains(e.Mark))
				{
					mc.Marks.Remove(e.Mark);
				}
			}
			PopulateMarkSnapTimes();
			SequenceModified();
		}

		private void timelineControl_RulerBeginDragTimeRange(object sender, EventArgs e)
		{
			_mPrevPlaybackStart = TimelineControl.PlaybackStartTime;
			_mPrevPlaybackEnd = TimelineControl.PlaybackEndTime;
		}

		private void timelineControl_TimeRangeDragged(object sender, ModifierKeysEventArgs e)
		{
			if (_context == null)
			{
				Logging.Error("TimedSequenceEditor: <timelineControl_TimeRangeDragged> - null context!");
				return;
			}

			bool autoPlay = e.ModifierKeys.HasFlag(Keys.Control);

			if (autoPlay)
			{
				if (TimelineControl.PlaybackStartTime != null && TimelineControl.PlaybackEndTime != null)
					_PlaySequence(TimelineControl.PlaybackStartTime.Value, TimelineControl.PlaybackEndTime.Value);
				else
				{
					Logging.Error("TimedSequenceEditor: <timelineControl_TimeRangeDragged> - On autoPlay, PlaybackStartTime or PlaybackEndTime was null!");
				}
			}
			else
			{
				// We actually want to keep this range.
				_mPrevPlaybackStart = TimelineControl.PlaybackStartTime;
				_mPrevPlaybackEnd = TimelineControl.PlaybackEndTime;
			}
		}

		#endregion

		#region Events

		//Create internal event for data being placed on clipboard as there is no outside data relevant
		//and monitoring the system clipboard gets into a bunch of not so pretty user32 api calls
		//So we will just deal with our own data. If other editors crop up that we can import data 
		//from via the clipboard, then this can be readdressed. This is mainly adding polish so we 
		//can set the enabled state of the paste menu items. JU 9/18/2012
		private static event EventHandler TimeLineSequenceClipboardContentsChanged;

		private void _TimeLineSequenceClipboardContentsChanged(EventArgs e)
		{
			if (TimeLineSequenceClipboardContentsChanged != null)
			{
				TimeLineSequenceClipboardContentsChanged(this, null);
			}
		}

		#endregion

		#region Sequence actions (play, pause, etc.)

		private void OnExecutionStateChanged(object sender, EventArgs e)
		{
			Console.WriteLine(@"tse: state changed: " + Execution.State);
			if (Execution.State.Equals("Closing"))
			{
				if (_context != null)
					CloseSequenceContext();
				_context = null;
			}
			else if (Execution.State.Equals("Open"))
			{
				OpenSequenceContext(_sequence);
			}
		}

		private void OpenSequenceContext(ISequence sequence)
		{
			if (_context != null)
			{
				CloseSequenceContext();
			}
			//_context = (ProgramContext)VixenSystem.Contexts.CreateContext(Sequence);
			//_context = VixenSystem.Contexts.CreateSequenceContext(new ContextFeatures(ContextCaching.ContextLevelCaching), Sequence);
			_context = VixenSystem.Contexts.CreateSequenceContext(new ContextFeatures(ContextCaching.NoCaching), Sequence);
			if (_context == null)
			{
				Logging.Error(@"TimedSequenceEditor: <OpenSequenceContext> - null _context when attempting to play sequence!");
				MessageBox.Show(@"Unable to play this sequence.  See error log for details.");
				return;
			}
			TimelineControl.grid.Context = _context;
			_context.SequenceStarted += context_SequenceStarted;
			_context.SequenceEnded += context_SequenceEnded;
			//_context.ProgramEnded += _context_ProgramEnded;
			_context.ContextEnded += context_ContextEnded;

			UpdateButtonStates();
		}

		private void CloseSequenceContext()
		{
			_context.SequenceStarted -= context_SequenceStarted;
			_context.SequenceEnded -= context_SequenceEnded;
			//_context.ProgramEnded -= _context_ProgramEnded;
			_context.ContextEnded -= context_ContextEnded;

			VixenSystem.Contexts.ReleaseContext(_context);
			UpdateButtonStates();
		}

		private void PlaySequence()
		{
			//MessageBox.Show("Call to play sequence");
			if (delayOffToolStripMenuItem.Checked == false && timerPostponePlay.Enabled == false && toolStripButton_Stop.Enabled == false)
			{
				//MessageBox.Show("Starting delay");
				_delayCountDown = (timerPostponePlay.Interval / 1000);
				timerPostponePlay.Enabled = timerDelayCountdown.Enabled = true;
				toolStripButton_Play.Image = Resources.hourglass;
				//The Looping stuff kinda broke this, but we need to do this for consistency
				toolStripButton_Play.Enabled = true;
				playToolStripMenuItem.Enabled = EffectsForm.Enabled = false;
				toolStripButton_Stop.Enabled = stopToolStripMenuItem.Enabled = true;
			}

			if (timerPostponePlay.Enabled)
			{
				//We are waiting for a delayed start, ignore the play button
				return;
			}

			//Make sure the blue play icon is used & dissappear the delay countdown
			toolStripButton_Play.Image = Resources.control_play_blue;
			toolStripStatusLabel3.Visible = toolStripStatusLabel_delayPlay.Visible = false;

			if (_context == null)
			{
				Logging.Error("TimedSequenceEditor: <PlaySequence> - attempt to Play with null _context!");
				return;
			}

			TimeSpan start, end;

			if (_context.IsPaused)
			{
				// continue execution from previous location.
				start = TimingSource.Position;
				end = TimeSpan.MaxValue;
				UpdateButtonStates(); // context provides no notification to/from pause state.
			}
			else
			{
				start = TimelineControl.PlaybackStartTime.GetValueOrDefault(TimeSpan.Zero);
				end = TimelineControl.PlaybackEndTime.GetValueOrDefault(TimeSpan.MaxValue);
			}
			_PlaySequence(start, end);
		}

		/// <summary>
		/// Plays the sequence from the specified starting point in TimeSpan format
		/// </summary>
		/// <param name="startTime"></param>
		public void PlaySequenceFrom(TimeSpan startTime)
		{
			if (_context == null)
			{
				Logging.Error("TimedSequenceEditor: <PlaySequenceFrom> - attempt to Play with null _context!");
				return;
			}

			TimeSpan start, end;

			if (_context.IsPaused)
			{
				// continue execution from previous location.
				start = TimingSource.Position;
				end = TimeSpan.MaxValue;
				UpdateButtonStates(); // context provides no notification to/from pause state.
			}
			else
			{
				start = startTime;
				end = TimelineControl.PlaybackEndTime.GetValueOrDefault(TimeSpan.MaxValue);
				if (start >= end)
				{
					start = TimelineControl.PlaybackStartTime.GetValueOrDefault(TimeSpan.Zero);
				}
			}
			_PlaySequence(start, end);
		}

		private void PauseSequence()
		{
			if (_context == null)
			{
				Logging.Error("TimedSequenceEditor: <PauseSequence> - attempt to Pause with null _context!");
				return;
			}

			_context.Pause();
			UpdateButtonStates(); // context provides no notification to/from pause state.
		}

		private void StopSequence()
		{
			if (delayOffToolStripMenuItem.Checked != true)
			{
				toolStripStatusLabel3.Visible = toolStripStatusLabel_delayPlay.Visible = true;
				toolStripStatusLabel_delayPlay.Text = string.Format("{0} Seconds", timerPostponePlay.Interval / 1000);
			}

			if (timerPostponePlay.Enabled)
			{
				timerPostponePlay.Enabled = timerDelayCountdown.Enabled = false;
				toolStripButton_Play.Image = Resources.control_play_blue;
				toolStripButton_Play.Enabled = playToolStripMenuItem.Enabled = EffectsForm.Enabled = true;
				toolStripButton_Stop.Enabled = stopToolStripMenuItem.Enabled = false;
				//We are stopping the delay, there is no context, so get out of here to avoid false entry into error log
				return;
			}

			if (_context == null)
			{
				Logging.Error("TimedSequenceEditor: <StopSequence> - attempt to Stop with null _context!");
				return;
			}

			_context.Stop();
			// button states updated by event handler.
		}

		protected void context_SequenceStarted(object sender, SequenceStartedEventArgs e)
		{
			timerPlaying.Start();
			TimingSource = e.TimingSource;
			UpdateButtonStates();
		}

		protected void context_SequenceEnded(object sender, SequenceEventArgs e)
		{
			//This is for the delayed play options
			if (delayOffToolStripMenuItem.Checked == false)
			{
				//MessageBox.Show("SHOWING STATUS BAR");
				toolStripStatusLabel3.Visible = toolStripStatusLabel_delayPlay.Visible = true;
				toolStripStatusLabel_delayPlay.Text = string.Format("{0} Seconds", timerPostponePlay.Interval / 1000);
			}

			timerPlaying.Stop();
			TimingSource = null;
		}

		protected void context_ContextEnded(object sender, EventArgs e)
		{
			UpdateButtonStates();

			TimelineControl.PlaybackStartTime = _mPrevPlaybackStart;
			TimelineControl.PlaybackEndTime = _mPrevPlaybackEnd;
			TimelineControl.PlaybackCurrentTime = null;
		}

		protected void timerPlaying_Tick(object sender, EventArgs e)
		{
			if (TimingSource != null)
			{
				TimelineControl.PlaybackCurrentTime = TimingSource.Position;
			}
		}

		private void timelineControl_PlaybackCurrentTimeChanged(object sender, EventArgs e)
		{
			toolStripStatusLabel_currentTime.Text = TimelineControl.PlaybackCurrentTime.HasValue ? TimelineControl.PlaybackCurrentTime.Value.ToString("m\\:ss\\.fff") : String.Empty;
		}

		private void CursorMovedHandler(object sender, EventArgs e)
		{
			var timeSpanEventArgs = e as TimeSpanEventArgs;
			if (timeSpanEventArgs != null)
				toolStripStatusLabel_currentTime.Text = timeSpanEventArgs.Time.ToString("m\\:ss\\.fff");
			else
			{
				Logging.Error("TimedSequenceEditor: <CursorMovedHandler> - timeSpanEventArgs = null!");
			}
		}

		private void UpdatePasteMenuStates()
		{
			toolStripButton_Paste.Enabled = toolStripMenuItem_Paste.Enabled = ClipboardHasData();
		}

		private bool ClipboardHasData()
		{
			IDataObject dataObject = Clipboard.GetDataObject();
			return dataObject != null && dataObject.GetDataPresent(ClipboardFormatName.Name);
		}

		private void UpdateButtonStates()
		{
			if (InvokeRequired)
				Invoke(new Delegates.GenericDelegate(UpdateButtonStates));
			else
			{
				if (_context == null)
				{
					toolStripButton_Play.Enabled = playToolStripMenuItem.Enabled = false;
					toolStripButton_Pause.Enabled = pauseToolStripMenuItem.Enabled = false;
					toolStripButton_Stop.Enabled = stopToolStripMenuItem.Enabled = false;
					EffectsForm.Enabled = false;
					return;
				}

				if (_context.IsRunning)
				{
					if (_context.IsPaused)
					{
						toolStripButton_Play.Enabled = playToolStripMenuItem.Enabled = true;
						toolStripButton_Pause.Enabled = pauseToolStripMenuItem.Enabled = false;
					}
					else
					{
						toolStripButton_Play.Enabled = playToolStripMenuItem.Enabled = false;
						toolStripButton_Pause.Enabled = pauseToolStripMenuItem.Enabled = true;
					}
					toolStripButton_Stop.Enabled = stopToolStripMenuItem.Enabled = true;
					EffectsForm.Enabled = false;
				}
				else // Stopped
				{
					toolStripButton_Play.Enabled = playToolStripMenuItem.Enabled = true;
					toolStripButton_Pause.Enabled = pauseToolStripMenuItem.Enabled = false;
					toolStripButton_Stop.Enabled = stopToolStripMenuItem.Enabled = false;
					EffectsForm.Enabled = true;
				}
			}
		}

		#endregion

		#region Sequence / TimelineControl relationship management

		private List<Element> CloneElements(IEnumerable<Element> elements)
		{
			var newElements = new List<Element>();
			foreach (var element in elements)
			{
				var newEffect = ApplicationServices.Get<IEffectModuleInstance>(element.EffectNode.Effect.TypeId);
				newEffect.ModuleData = element.EffectNode.Effect.ModuleData.Clone();
				
				try
				{
					// get the target element
					if (element.Row != null)
					{
						var targetNode = (ElementNode) element.Row.Tag;

						// populate the given effect instance with the appropriate target node and times, and wrap it in an effectNode
						newEffect.TargetNodes = new[] {targetNode};
					}
					else
					{
						Logging.Error("TimedSequenceEditor: <CloneElements> - Skipping element; element.Row is null!");
						continue;
					}

					newEffect.TimeSpan = element.Duration;
					var effectNode = new EffectNode(newEffect, element.StartTime);

					// put it in the sequence and in the timeline display
					newElements.Add(AddEffectNode(effectNode));
					

				} catch (Exception ex)
				{
					string msg = "TimedSequenceEditor CloneElements: error adding effect of type " + newEffect.Descriptor.TypeId + " to row " +
								 ((element.Row == null) ? "<null>" : element.Row.Name);
					Logging.Error(msg, ex);
				}
			}

			SequenceModified();

			//Add elements as a group to undo
			var act = new EffectsAddedUndoAction(this, newElements.Select(x => x.EffectNode).ToArray());
			_undoMgr.AddUndoAction(act);

			return newElements;
		}

		/// <summary>
		/// Adds an EffectNode to the sequence and the TimelineControl.
		/// </summary>
		/// <param name="node"></param>
		/// <returns>The TimedSequenceElement created and added to the TimelineControl.</returns>
		public TimedSequenceElement AddEffectNode(EffectNode node)
		{
			//Debug.WriteLine("{0}   AddEffectNode({1})", (int)DateTime.Now.TimeOfDay.TotalMilliseconds, node.Effect.InstanceId);
			_sequence.InsertData(node);
			//return addElementForEffectNode(node);
			return AddElementForEffectNodeTpl(node);
		}

		/// <summary>
		/// Adds multiple EffectNodes to the sequence and the TimelineControl.
		/// </summary>
		/// <param name="nodes"></param>
		/// <returns>A List of the TimedSequenceElements created and added to the TimelineControl.</returns>
		private List<TimedSequenceElement> AddEffectNodes(IEnumerable<EffectNode> nodes)
		{
			return nodes.Select(AddEffectNode).ToList();
		}

		/// <summary>
		/// Selects the given effects given in an EffectNode list
		/// </summary>
		///<param name="nodes"></param>
		private void SelectEffectNodes(IEnumerable<EffectNode> nodes)
		{
			TimelineControl.grid.ClearSelectedElements();

			foreach (EffectNode element in nodes)
			{
				TimedSequenceElement tse = (TimedSequenceElement)_effectNodeToElement[element];
				tse.Selected = true;
			}
		}
		
		/// <summary>
		/// Removes the Effect Node and Element
		/// </summary>
		/// <param name="node"></param>
		public void RemoveEffectNodeAndElement(EffectNode node)
		{
			//Debug.WriteLine("{0}   RemoveEffectNodeAndElement(InstanceId={1})", (int)DateTime.Now.TimeOfDay.TotalMilliseconds, node.Effect.InstanceId);

			// Lookup this effect node's Timeline Element
			TimedSequenceElement tse = (TimedSequenceElement)_effectNodeToElement[node];

			foreach (Row row in TimelineControl) // Remove the element from all rows
				row.RemoveElement(tse);

			// TODO: Unnecessary?
			tse.ContentChanged -= ElementContentChangedHandler; // Unregister event handlers
			tse.TimeChanged -= ElementTimeChangedHandler;

			_effectNodeToElement.Remove(node); // Remove the effect node from the map
			_sequence.RemoveData(node); // Remove the effect node from sequence
		}


		/// <summary>
		/// Creates a new effect instance, and adds it to the sequence and TimelineControl.
		/// </summary>
		/// <param name="effectId">The GUID of the effect module to instantiate</param>
		/// <param name="row">The Common.Controls.Timeline.Row to add the effect to</param>
		/// <param name="startTime">The start time of the effect</param>
		/// <param name="timeSpan">The duration of the effect</param>
		private void AddNewEffectById(Guid effectId, Row row, TimeSpan startTime, TimeSpan timeSpan)
		{
			//Debug.WriteLine("{0}   addNewEffectById({1})", (int)DateTime.Now.TimeOfDay.TotalMilliseconds, effectId);
			// get a new instance of this effect, populate it, and make a node for it

			IEffectModuleInstance effect = ApplicationServices.Get<IEffectModuleInstance>(effectId);
			AddEffectInstance(effect, row, startTime, timeSpan);
		}

		/// <summary>
		/// Wraps an effect instance in an EffectNode, adds it to the sequence, and an associated element to the timeline control.
		/// Adds a Undo record for the add as well.
		/// </summary>
		/// <param name="effectInstance">Effect instance</param>
		/// <param name="row">Common.Controls.Timeline.Row to add the effect instance to</param>
		/// <param name="startTime">The start time of the effect</param>
		/// <param name="timeSpan">The duration of the effect</param>
		/// <param name="parameterValues">Optional ParameterValues</param>
		private void AddEffectInstance(IEffectModuleInstance effectInstance, Row row, TimeSpan startTime, TimeSpan timeSpan, object[] parameterValues = null)
		{
			try
			{
				//Debug.WriteLine("{0}   addEffectInstance(InstanceId={1})", (int)DateTime.Now.TimeOfDay.TotalMilliseconds, effectInstance.InstanceId);

				if ((startTime + timeSpan) > SequenceLength)
				{
					timeSpan = SequenceLength - startTime;
				}

				var effectNode = CreateEffectNode(effectInstance, row, startTime, timeSpan);
				// set the option parametervalues if not null
				if (parameterValues != null) effectNode.Effect.ParameterValues = parameterValues;
				// put it in the sequence and in the timeline display
				AddEffectNode(effectNode);
				SequenceModified();

				var act = new EffectsAddedUndoAction(this, new[] { effectNode });
				_undoMgr.AddUndoAction(act);
			}
			catch (Exception ex)
			{
				string msg = "TimedSequenceEditor: error adding effect of type " + effectInstance.Descriptor.TypeId + " to row " +
							 ((row == null) ? "<null>" : row.Name);
				Logging.Error(msg, ex);
			}
		}

		private static EffectNode CreateEffectNode(IEffectModuleInstance effectInstance, Row row, TimeSpan startTime,
			TimeSpan timeSpan, object[] parameterValues = null)
		{
			// get the target element
			var targetNode = (ElementNode) row.Tag;

			// populate the given effect instance with the appropriate target node and times, and wrap it in an effectNode
			effectInstance.TargetNodes = new[] {targetNode};
			effectInstance.TimeSpan = timeSpan;
			if (parameterValues != null) effectInstance.ParameterValues = parameterValues;
			return new EffectNode(effectInstance, startTime);
	
		}


		/// <summary>
		/// Populates the TimelineControl grid with a new TimedSequenceElement for each of the given EffectNodes in the list.
		/// Uses bulk loading feature of Row
		/// Will add a single TimedSequenceElement to in each row that each targeted element of
		/// the EffectNode references. It will also add callbacks to event handlers for the element.
		/// </summary>
		/// <param name="nodes">The EffectNode to make element(s) in the grid for.</param>
		private void AddElementsForEffectNodes(IEnumerable<IDataNode> nodes)
		{
			Dictionary<Row, List<Element>> rowMap =
			_elementNodeToRows.SelectMany(x => x.Value).ToList().ToDictionary(x => x, x => new List<Element>());

			foreach (EffectNode node in nodes)
			{
				TimedSequenceElement element = SetupNewElementFromNode(node);
				foreach (ElementNode target in node.Effect.TargetNodes)
				{
					if (_elementNodeToRows.ContainsKey(target))
					{
						// Add the element to each row that represents the element this command is in.
						foreach (Row row in _elementNodeToRows[target])
						{
							if (!_effectNodeToElement.ContainsKey(node))
							{
								_effectNodeToElement[node] = element;
							}
							rowMap[row].Add(element);

						}
					}
					else
					{
						// we don't have a row for the element this effect is referencing; most likely, the row has
						// been deleted, or we're opening someone else's sequence, etc. Big fat TODO: here for that, then.
						// dunno what we want to do: prompt to add new elements for them? map them to others? etc.
						const string message = "TimedSequenceEditor: <AddElementsForEffectNodes> - No Timeline.Row is associated with a target ElementNode for this EffectNode. It now exists in the sequence, but not in the GUI.";
						Logging.Error(message);
						MessageBox.Show(message);
					}
				}
			}

			foreach (KeyValuePair<Row, List<Element>> row in rowMap)
			{
				row.Key.AddBulkElements(row.Value);
			}

		}

		/// <summary>
		/// Populates the TimelineControl grid with a new TimedSequenceElement for the given EffectNode.
		/// Will add a single TimedSequenceElement to in each row that each targeted element of
		/// the EffectNode references. It will also add callbacks to event handlers for the element.
		/// </summary>
		/// <param name="node">The EffectNode to make element(s) in the grid for.</param>
		private TimedSequenceElement AddElementForEffectNodeTpl(EffectNode node)
		{
			TimedSequenceElement element = SetupNewElementFromNode(node);

			// for the effect, make a single element and add it to every row that represents its target elements
			node.Effect.TargetNodes.AsParallel().WithCancellation(_cancellationTokenSource.Token)
				.ForAll(target =>
							{
								if (_elementNodeToRows.ContainsKey(target))
								{
									// Add the element to each row that represents the element this command is in.
									foreach (Row row in _elementNodeToRows[target])
									{
										if (!_effectNodeToElement.ContainsKey(node))
										{
											_effectNodeToElement[node] = element;
										}
										row.AddElement(element);
									}
								}
								else
								{
									// we don't have a row for the element this effect is referencing; most likely, the row has
									// been deleted, or we're opening someone else's sequence, etc. Big fat TODO: here for that, then.
									// dunno what we want to do: prompt to add new elements for them? map them to others? etc.
									const string message = "TimedSequenceEditor: <AddElementForEffectNodeTpl> - No Timeline.Row is associated with a target ElementNode for this EffectNode. It now exists in the sequence, but not in the GUI.";
									Logging.Error(message);
									MessageBox.Show(message);
								}
							});
			TimelineControl.grid.RenderElement(element);
			return element;
		}

		private TimedSequenceElement SetupNewElementFromNode(EffectNode node)
		{
			TimedSequenceElement element = new TimedSequenceElement(node);
			element.ContentChanged += ElementContentChangedHandler;
			element.TimeChanged += ElementTimeChangedHandler;
			return element;
		}

		/// <summary>
		/// Checks all elements and if they are dirty they are placed in the render queue
		/// </summary>
		private void CheckAndRenderDirtyElements()
		{
			TimelineControl.Rows.AsParallel().WithCancellation(_cancellationTokenSource.Token).ForAll(target =>
			{
				foreach (Element elem in target)
				{
					if (elem.EffectNode.Effect.IsDirty)
					{
						TimelineControl.grid.RenderElement(elem);
					}
				}
			});
		}

		private void RemoveSelectedElements()
		{
			Element[] selected = TimelineControl.SelectedElements.ToArray();

			if (selected.Length == 0)
				return;

			// Add the undo action
			var action = new EffectsRemovedUndoAction(this,
													  selected.Cast<TimedSequenceElement>().Select(x => x.EffectNode)
				);
			_undoMgr.AddUndoAction(action);

			// Remove the elements (sequence and GUI)
			foreach (TimedSequenceElement elem in selected)
			{
				RemoveEffectNodeAndElement(elem.EffectNode);
			}

			SequenceModified();
		}


		private int _doEventsCounter;

		/// <summary>
		/// Adds a single given element node as a row in the timeline control. Recursively adds all
		/// child nodes of the given node as children, if needed.
		/// </summary>
		/// <param name="node">The node to generate a row for.</param>
		/// <param name="parentRow">The parent node the row should belong to, if any.</param>
		private void AddNodeAsRow(ElementNode node, Row parentRow)
		{
			// made the new row from the given node and add it to the control.
			TimedSequenceRowLabel label = new TimedSequenceRowLabel {Name = node.Name};
			Row newRow = TimelineControl.AddRow(label, parentRow, 32);
			newRow.ElementRemoved += ElementRemovedFromRowHandler;
			newRow.ElementAdded += ElementAddedToRowHandler;

			// Tag it with the node it refers to, and take note of which row the given element node will refer to.
			newRow.Tag = node;
			if (_elementNodeToRows.ContainsKey(node))
				_elementNodeToRows[node].Add(newRow);
			else
				_elementNodeToRows[node] = new List<Row> { newRow };

			// This slows the load down just a little, but it
			// allows the update of the load timer on the bottom of the 
			// screen so Vixen doesn't appear to be locked up for very large sequences
			if (_doEventsCounter % 600 == 0)
				Application.DoEvents();
			_doEventsCounter++;

			// iterate through all if its children, adding them as needed
			foreach (ElementNode child in node.Children)
			{
				AddNodeAsRow(child, newRow);
			}
		}

		#endregion

		#region Effect & Preset Library Drag/Drop

		private void timelineControl_DataDropped(object sender, TimelineDropEventArgs e)
		{
			//Modified 12-3-2014 to allow Control-Drop of effects to replace selected effects
			Guid effectGuid = (Guid)e.Data.GetData(DataFormats.Serializable);
			TimeSpan duration = TimeSpan.FromSeconds(2.0); // TODO: need a default value here. I suggest a per-effect default.
			TimeSpan startTime = Util.Min(e.Time, (_sequence.Length - duration)); // Ensure the element is inside the grid.

			if (ModifierKeys.HasFlag(Keys.Control) && TimelineControl.SelectedElements.Any())
			{

				var message = string.Format("This action will replace {0} effects, are you sure ?",
					TimelineControl.SelectedElements.Count());
				var result = MessageBox.Show(message, @"Replace existing effects?", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
				if (result == DialogResult.No)
				{
					return;
				}

				var newEffects = (from elem in TimelineControl.SelectedElements let newEffectInstance = ApplicationServices.Get<IEffectModuleInstance>(effectGuid) select CreateEffectNode(newEffectInstance, elem.Row, elem.StartTime, elem.Duration)).ToList();

				RemoveSelectedElements();
				AddEffectNodes(newEffects);
				SelectEffectNodes(newEffects);

				//Add the undo action for the newly created effects
				var act = new EffectsAddedUndoAction(this, newEffects);
				_undoMgr.AddUndoAction(act);
			}
			else
			{
			AddNewEffectById(effectGuid, e.Row, startTime, duration);
		}
		}

		private Tuple<List<Element>,bool> BuildElementListForItemDrop(Element e)
		{
			List<Element> elementList = new List<Element>();
			bool multipleEffectTypes = false;

			if (e.Selected)
			{
				foreach (Element elem in TimelineControl.SelectedElements)
				{
					if (elem.EffectNode.Effect.TypeId == e.EffectNode.Effect.TypeId)
					{
						elementList.Add(elem);
					}
					else
					{
						multipleEffectTypes = true;
					}
				}
			}
			else
			{
				elementList.Add(e);
			}

			return new Tuple<List<Element>, bool>(elementList,multipleEffectTypes);
		}

		#region Preset Library Color Drop
		private void timelineControl_ColorDropped(object sender, ToolDropEventArgs e)
		{
			Tuple<List<Element>, bool> getElementList = BuildElementListForItemDrop(e.Element);
			List<Element> elementList = getElementList.Item1;
			var multipleEffectTypes = getElementList.Item2;
			List<EffectParameterPickerControl> parameterPickerControls = new List<EffectParameterPickerControl>();
			Color color = (Color)e.Data.GetData(typeof(Color));
			var mousePosition = MousePosition; //Position of mouse when drop happened
			var hasColor = false;
			var isColorGradient = false;
			var isColorList = false;
			var isColorGradientList = false;

			if (multipleEffectTypes)
			{
				var dr =
					MessageBox.Show(@"Multiple type effects selected, this will only apply to effects of the type: " +
									e.Element.EffectNode.Effect.EffectName, @"Multiple Type Effects", MessageBoxButtons.OKCancel);
				
				if (dr == DialogResult.Cancel) return;
			}

			int i = 0;
			//Seach for typeof Color
			foreach (ParameterSpecification pSig in e.Element.EffectNode.Effect.Parameters)
			{
				if (pSig.Type == typeof (Color))
				{
					hasColor = true;

					EffectParameterPickerControl effectParameterPickerControl = new EffectParameterPickerControl
					{
						ParameterIndex = i,
						ParameterName = pSig.Name,
						ParameterImage = getColorBitmap((Color)e.Element.EffectNode.Effect.ParameterValues[i])
					};
					
					parameterPickerControls.Add(effectParameterPickerControl);
				}
				i++;
			}


			//Seach for typeof ColorGradient
			if (parameterPickerControls.Count == 0)
			{
				i = 0;
				foreach (ParameterSpecification pSig in e.Element.EffectNode.Effect.Parameters)
				{
					if (pSig.Type == typeof(ColorGradient))
					{
						hasColor = isColorGradient = true;

						EffectParameterPickerControl effectParameterPickerControl = new EffectParameterPickerControl
						{
							ParameterIndex = i,
							ParameterName = pSig.Name,
							ParameterImage = getColorGradientBitmap((ColorGradient)e.Element.EffectNode.Effect.ParameterValues[i])
						};

						parameterPickerControls.Add(effectParameterPickerControl);
					}
					i++;
				}
			}

			//Seach for typeof List<Color>
			if (parameterPickerControls.Count == 0)
			{
				i = 0;
				foreach (ParameterSpecification pSig in e.Element.EffectNode.Effect.Parameters)
				{
					if (pSig.Type == typeof (List<Color>))
					{
						hasColor = isColorList = true;

						List<Color> colorList = (List<Color>) e.Element.EffectNode.Effect.ParameterValues[i];

						int colorIndex = 0;
						foreach (Color colorItem in colorList)
						{
							EffectParameterPickerControl effectParameterPickerControl = new EffectParameterPickerControl
							{
								ParameterIndex = i,
								ParameterListIndex = colorIndex,
								ParameterName = "Color " + (colorIndex + 1),
								ParameterImage = getColorBitmap(colorItem)
							};

							parameterPickerControls.Add(effectParameterPickerControl);
							colorIndex++;
						}
					}
					i++;
				}

			}

			//Seach for typeof List<ColorGradient>
			if (parameterPickerControls.Count == 0)
			{
				i = 0;
				foreach (ParameterSpecification pSig in e.Element.EffectNode.Effect.Parameters)
				{
					if (pSig.Type == typeof(List<ColorGradient>))
					{
						hasColor = isColorGradientList = true;

						List<ColorGradient> gradientList = (List<ColorGradient>)e.Element.EffectNode.Effect.ParameterValues[i];

						int gradientIndex = 0;
						foreach (ColorGradient gradientItem in gradientList)
						{
							EffectParameterPickerControl effectParameterPickerControl = new EffectParameterPickerControl
							{
								ParameterIndex = i,
								ParameterListIndex = gradientIndex,
								ParameterName = "ColorGradient " + (gradientIndex + 1),
								ParameterImage = getColorGradientBitmap(gradientItem)
							};

							parameterPickerControls.Add(effectParameterPickerControl);
							gradientIndex++;
						}
					}
					i++;
				}
			}

			if (parameterPickerControls.Count > 1 || isColorList || isColorGradientList)
			{
				FormParameterPicker parameterPicker = new FormParameterPicker(parameterPickerControls)
				{
					StartPosition = FormStartPosition.Manual,
					Top = mousePosition.Y
				};
				parameterPicker.Left = ((mousePosition.X + parameterPicker.Width) < Screen.PrimaryScreen.Bounds.Width)
					? mousePosition.X
					: mousePosition.X - parameterPicker.Width;

				UpdateToolStrip4("Chose the color property to set, press Escape to cancel.");
				var dr = parameterPicker.ShowDialog();
				if (dr == DialogResult.OK)
				{
					int k = 1;
					if (isColorList)
					{
						foreach (Element elem in elementList)
						{
							object[] parms = elem.EffectNode.Effect.ParameterValues;
							List<Color> colorList = (List<Color>)parms[parameterPicker.ParameterIndex];
							if (parameterPicker.ParameterListIndex >= colorList.Count) continue;							
							colorList[parameterPicker.ParameterListIndex] = color;
							parms[parameterPicker.ParameterIndex] = colorList;
							elem.EffectNode.Effect.ParameterValues = parms;
							TimelineControl.grid.RenderElement(elem);
							SequenceModified();
							k++;
						}
					}

					if (isColorGradientList)
					{
						foreach (Element elem in elementList)
						{
							object[] parms = elem.EffectNode.Effect.ParameterValues;
							List<ColorGradient> colorGradientList = (List<ColorGradient>) parms[parameterPicker.ParameterIndex];
							if (parameterPicker.ParameterListIndex >= colorGradientList.Count) continue;
							colorGradientList[parameterPicker.ParameterListIndex] = new ColorGradient(color);
							parms[parameterPicker.ParameterIndex] = colorGradientList;
							elem.EffectNode.Effect.ParameterValues = parms;
							TimelineControl.grid.RenderElement(elem);
							SequenceModified();
							k++;
						}
					}

					if (!isColorList && !isColorGradientList)
					{
						foreach (Element elem in elementList)
						{
							object[] parms = elem.EffectNode.Effect.ParameterValues;
							parms[parameterPicker.ParameterIndex] = color;

							switch (elem.EffectNode.Effect.EffectName)
							{
								case "Alternating":
									if (parameterPicker.ParameterIndex == 1)
										parms[8] = true;
									else
										parms[9] = true;
									break;
							}

							elem.EffectNode.Effect.ParameterValues = parms;
							TimelineControl.grid.RenderElement(elem);
							SequenceModified();
							k++;
						}
					}
					
					UpdateToolStrip4("Color applied to " + k + " " + e.Element.EffectNode.Effect.EffectName + " effect(s).",60);
				}
				else
				{
					UpdateToolStrip4("Color drop action cancled, no effects were modified.",60);
				}
			}
			else
			{
				if (!hasColor)
				{
					UpdateToolStrip4("No action taken, the target effect(s) do not support colors.",60);
					return;
				}

				int j = !isColorGradient ? e.Element.EffectNode.Effect.Parameters.TakeWhile(pSig => pSig.Type != typeof(Color)).Count() : e.Element.EffectNode.Effect.Parameters.TakeWhile(pSig => pSig.Type != typeof(ColorGradient)).Count();

				int k = 0;
				foreach (Element elem in elementList)
				{
					object[] parms = elem.EffectNode.Effect.ParameterValues;
					if (!isColorGradient)
					{
						parms[j] = color;
					}
					else
					{
						parms[j] = new ColorGradient(color);	
					}

					switch (elem.EffectNode.Effect.EffectName)
					{
						case "Chase":
							parms[0] = 0;
							break;
						case "Spin":
							parms[2] = 0;
							break;
						case "Twinkle":
							parms[7] = 0;
							break;
					}

					elem.EffectNode.Effect.ParameterValues = parms;
					TimelineControl.grid.RenderElement(elem);
					SequenceModified();
					k++;
				}

				UpdateToolStrip4("Color applied to " + k + " " + e.Element.EffectNode.Effect.EffectName + " effect(s).",60);
			}
		}

		#endregion Preset Library Color Drop

		#region Preset Library Curve Drop
		private void timelineControl_CurveDropped(object sender, ToolDropEventArgs e)
		{
			Tuple<List<Element>, bool> getElementList = BuildElementListForItemDrop(e.Element);
			List<Element> elementList = getElementList.Item1;
			var multipleEffectTypes = getElementList.Item2;
			List<EffectParameterPickerControl> parameterPickerControls = new List<EffectParameterPickerControl>();
			Curve curve = new Curve(_curveLibrary.GetCurve(e.Data.GetData(DataFormats.StringFormat).ToString()));
			var mousePosition = MousePosition; //Position of mouse when drop happened
			var hasCurve = false;
			var isCurveList = false;

			if (multipleEffectTypes)
			{
				var dr =
					MessageBox.Show(@"Multiple type effects selected, this will only apply to effects of the type: " +
					                e.Element.EffectNode.Effect.EffectName, @"Multiple Type Effects", MessageBoxButtons.OKCancel);
				
				if (dr == DialogResult.Cancel) return;
			}

			if (ToolsForm.LinkCurves)
			{
				curve.LibraryReferenceName = e.Data.GetData(DataFormats.StringFormat).ToString();
			}
			else
			{
				curve.LibraryReferenceName = string.Empty;
				curve.UnlinkFromLibraryCurve();
			}

			curve.IsCurrentLibraryCurve = false;
			
			int i = 0;			
			foreach (ParameterSpecification pSig in e.Element.EffectNode.Effect.Parameters)
			{
				if (pSig.Type == typeof (Curve))
				{
					hasCurve = true;

					EffectParameterPickerControl effectParameterPickerControl = new EffectParameterPickerControl
					{
						ParameterIndex = i,
						ParameterName = pSig.Name,
						ParameterImage = getCurveBitmap((Curve)e.Element.EffectNode.Effect.ParameterValues[i])
					};

					parameterPickerControls.Add(effectParameterPickerControl);
				}
				i++;
			}

			//Seach for typeof List<Curve>
			if (parameterPickerControls.Count == 0)
			{
				i = 0;
				foreach (ParameterSpecification pSig in e.Element.EffectNode.Effect.Parameters)
				{
					if (pSig.Type == typeof(List<Curve>))
					{
						hasCurve = isCurveList = true;

						List<Curve> curveList = (List<Curve>)e.Element.EffectNode.Effect.ParameterValues[i];

						int curveIndex = 0;
						foreach (Curve curveItem in curveList)
						{
							EffectParameterPickerControl effectParameterPickerControl = new EffectParameterPickerControl
							{
								ParameterIndex = i,
								ParameterListIndex = curveIndex,
								ParameterName = "Curve " + (curveIndex + 1),
								ParameterImage = getCurveBitmap(curveItem)
							};

							parameterPickerControls.Add(effectParameterPickerControl);
							curveIndex++;
						}
					}
					i++;
				}
			}

			if (parameterPickerControls.Count > 1)
			{
				FormParameterPicker parameterPicker = new FormParameterPicker(parameterPickerControls)
				{
					StartPosition = FormStartPosition.Manual,
					Top = mousePosition.Y
				};
				parameterPicker.Left = ((mousePosition.X + parameterPicker.Width) < Screen.PrimaryScreen.Bounds.Width)
					? mousePosition.X
					: mousePosition.X - parameterPicker.Width;

				UpdateToolStrip4("Chose the Curve property to set, press Escape cancel.");
				var dr = parameterPicker.ShowDialog();
				if (dr == DialogResult.OK)
				{
					int k = 0;
					if (isCurveList)
					{
						foreach (Element elem in elementList)
						{
							object[] parms = elem.EffectNode.Effect.ParameterValues;
							List<Curve> curveList = (List<Curve>)parms[parameterPicker.ParameterIndex];
							if (parameterPicker.ParameterListIndex >= curveList.Count) continue;
							curveList[parameterPicker.ParameterListIndex] = new Curve(curve);
							parms[parameterPicker.ParameterIndex] = curveList;
							elem.EffectNode.Effect.ParameterValues = parms;
							TimelineControl.grid.RenderElement(elem);
							SequenceModified();
							k++;
						}
					}
					
					else //!isCurveList
					{
						foreach (Element elem in elementList)
						{
							object[] parms = elem.EffectNode.Effect.ParameterValues;
							parms[parameterPicker.ParameterIndex] = curve;
							elem.EffectNode.Effect.ParameterValues = parms;
							TimelineControl.grid.RenderElement(elem);
							SequenceModified();
							k++;
						}
					}
					
					UpdateToolStrip4("Curve " + e.Data.GetData(DataFormats.StringFormat) + " applied to " + k + " " + e.Element.EffectNode.Effect.EffectName + " effect(s).",60);
				}
				else
				{
					UpdateToolStrip4("Curve drop action cancled, no effects were modified.",60);
				}
			}
			else
			{
				if (!hasCurve)
				{
					UpdateToolStrip4("No action taken, the target effect(s) do not support curves.",60);
					return;
				}

				int j = e.Element.EffectNode.Effect.Parameters.TakeWhile(pSig => pSig.Type != typeof (Curve)).Count();

				int k = 0;
				foreach (Element elem in elementList)
				{
					object[] parms = elem.EffectNode.Effect.ParameterValues;
					parms[j] = curve;
					elem.EffectNode.Effect.ParameterValues = parms;
					TimelineControl.grid.RenderElement(elem);
					SequenceModified();
					k++;
				}

				UpdateToolStrip4("Curve " + e.Data.GetData(DataFormats.StringFormat) + " applied to " + k + " " + e.Element.EffectNode.Effect.EffectName + " effect(s).",60);
			}
		}

		#endregion Preset Library Curve Drop

		#region Preset Library Color Gradient Drop
		private void timelineControl_GradientDropped(object sender, ToolDropEventArgs e)
		{
			Tuple<List<Element>, bool> getElementList = BuildElementListForItemDrop(e.Element);
			List<Element> elementList = getElementList.Item1;
			var multipleEffectTypes = getElementList.Item2;
			List<EffectParameterPickerControl> parameterPickerControls = new List<EffectParameterPickerControl>();
			ColorGradient colorGradient = new ColorGradient(_colorGradientLibrary.GetColorGradient(e.Data.GetData(DataFormats.StringFormat).ToString()));
			var mousePosition = MousePosition; //Position of mouse when drop happened
			var hasColorGradient = false;
			var isColorGradientList = false;

			if (multipleEffectTypes)
			{
				var dr =
					MessageBox.Show(@"Multiple type effects selected, this will only apply to effects of the type: " +
									e.Element.EffectNode.Effect.EffectName, @"Multiple Type Effects", MessageBoxButtons.OKCancel);

				if (dr == DialogResult.Cancel) return;
			}

			if (ToolsForm.LinkGradients)
			{
				colorGradient.LibraryReferenceName = e.Data.GetData(DataFormats.StringFormat).ToString();
			}
			else
			{
				colorGradient.LibraryReferenceName = string.Empty;
				colorGradient.UnlinkFromLibrary();
			}

			colorGradient.IsCurrentLibraryGradient = false;

			int i = 0;
			foreach (ParameterSpecification pSig in e.Element.EffectNode.Effect.Parameters)
			{
				if (pSig.Type == typeof(ColorGradient))
				{
					hasColorGradient = true;

					EffectParameterPickerControl effectParameterPickerControl = new EffectParameterPickerControl
					{
						ParameterIndex = i,
						ParameterName = pSig.Name,
						ParameterImage = getColorGradientBitmap((ColorGradient)e.Element.EffectNode.Effect.ParameterValues[i])
					};

					parameterPickerControls.Add(effectParameterPickerControl);
				}
				i++;
			}

			//Seach for typeof List<ColorGradient>
			if (parameterPickerControls.Count == 0)
			{
				i = 0;
				foreach (ParameterSpecification pSig in e.Element.EffectNode.Effect.Parameters)
				{
					if (pSig.Type == typeof(List<ColorGradient>))
					{
						hasColorGradient = isColorGradientList = true;

						List<ColorGradient> gradientList = (List<ColorGradient>)e.Element.EffectNode.Effect.ParameterValues[i];

						int gradientIndex = 0;
						foreach (ColorGradient gradientItem in gradientList)
						{
							EffectParameterPickerControl effectParameterPickerControl = new EffectParameterPickerControl
							{
								ParameterIndex = i,
								ParameterListIndex = gradientIndex,
								ParameterName = "ColorGradient " + (gradientIndex + 1),
								ParameterImage = getColorGradientBitmap(gradientItem)
							};

							parameterPickerControls.Add(effectParameterPickerControl);
							gradientIndex++;
						}
					}
					i++;
				}
			}

			if (parameterPickerControls.Count > 1)
			{
				FormParameterPicker parameterPicker = new FormParameterPicker(parameterPickerControls)
				{
					StartPosition = FormStartPosition.Manual,
					Top = mousePosition.Y
				};
				parameterPicker.Left = ((mousePosition.X + parameterPicker.Width) < Screen.PrimaryScreen.Bounds.Width)
					? mousePosition.X
					: mousePosition.X - parameterPicker.Width;

				UpdateToolStrip4("Chose the Color Gradient property to set, press Escape to cancel.");
				var dr = parameterPicker.ShowDialog();
				if (dr == DialogResult.OK)
				{
					int k = 0;
					if (isColorGradientList)
					{
						foreach (Element elem in elementList)
						{
							object[] parms = elem.EffectNode.Effect.ParameterValues;
							List<ColorGradient> colorGradientList = (List<ColorGradient>)parms[parameterPicker.ParameterIndex];
							if (parameterPicker.ParameterListIndex >= colorGradientList.Count) continue;
							colorGradientList[parameterPicker.ParameterListIndex] = new ColorGradient(colorGradient);
							parms[parameterPicker.ParameterIndex] = colorGradientList;
							elem.EffectNode.Effect.ParameterValues = parms;
							TimelineControl.grid.RenderElement(elem);
							SequenceModified();
							k++;
						}
					}
					
					else
					{
						foreach (Element elem in elementList)
						{
							object[] parms = elem.EffectNode.Effect.ParameterValues;
							parms[parameterPicker.ParameterIndex] = colorGradient;

							switch (elem.EffectNode.Effect.EffectName)
							{
								case "Alternating":
									if (parameterPicker.ParameterIndex == 10)
										parms[8] = false;
									else
										parms[9] = false;
									break;
							}

							elem.EffectNode.Effect.ParameterValues = parms;
							TimelineControl.grid.RenderElement(elem);
							SequenceModified();
							k++;
						}
					}

					UpdateToolStrip4("Color Gradient " + e.Data.GetData(DataFormats.StringFormat) + " applied to " + k + " " + e.Element.EffectNode.Effect.EffectName + " effect(s).",60);
				}
				else
				{
					UpdateToolStrip4("Color Gradient drop action cancled, no effects were modified.",60);
				}
			}
			else
			{
				if (!hasColorGradient)
				{
					UpdateToolStrip4("No action taken, the target effect(s) do not support Color Gradients.",60);
					return;
				}

				int j = e.Element.EffectNode.Effect.Parameters.TakeWhile(pSig => pSig.Type != typeof(ColorGradient)).Count();

				int k = 0;
				foreach (Element elem in elementList)
				{
					object[] parms = elem.EffectNode.Effect.ParameterValues;
					parms[j] = colorGradient;
					switch (elem.EffectNode.Effect.EffectName)
					{
						case "Chase":
							parms[0] = ToolsForm.GradientHandling;
							break;
						case "Spin":
							parms[2] = ToolsForm.GradientHandling;
							break;
						case "Twinkle":
							parms[7] = ToolsForm.GradientHandling;
							break;
					}
					elem.EffectNode.Effect.ParameterValues = parms;
					TimelineControl.grid.RenderElement(elem);
					SequenceModified();
					k++;
				}

				UpdateToolStrip4("Color Gradient " + e.Data.GetData(DataFormats.StringFormat) + " applied to " + k + " " + e.Element.EffectNode.Effect.EffectName + " effect(s).",60);
			}
		}
		
		#endregion Preset Library Color Gradient Drop

		#region Bitmap methods for PL item drops

		private Bitmap getColorBitmap(Color color)
		{
			Bitmap colorBitmap = new Bitmap(48, 48);
			Graphics gfx = Graphics.FromImage(colorBitmap);
			using (SolidBrush brush = new SolidBrush(color))
			{
				gfx.FillRectangle(brush, 0, 0, 48, 48);
				gfx.DrawRectangle(new Pen(Color.Black, 2), 0, 0, 48, 48);
			}

			return colorBitmap;
		}

		private Bitmap getCurveBitmap(Curve curve)
		{
			var curveBitmap = new Bitmap((curve.GenerateCurveImage(new Size(48, 48))));
			Graphics gfx = Graphics.FromImage(curveBitmap);
			gfx.DrawRectangle(new Pen(Color.Black, 2), 0, 0, 48, 48);

			return curveBitmap;
		}

		private Bitmap getColorGradientBitmap(ColorGradient colorGradient)
		{
			var gradientBitmap = new Bitmap((colorGradient.GenerateColorGradientImage(new Size(48, 48), false)));
			Graphics gfx = Graphics.FromImage(gradientBitmap);
			gfx.DrawRectangle(new Pen(Color.Black, 2), 0, 0, 48, 48);

			return gradientBitmap;
		}

		#endregion Bitmap methods for PL item drops

		#endregion

		#region Overridden form functions (On___)

		protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
		{
			switch (keyData)
			{
				case Keys.Left:
					TimelineControl.ruler.NudgeMark(-TimelineControl.ruler.StandardNudgeTime);
					break;
				case (Keys.Left | Keys.Shift):
					TimelineControl.ruler.NudgeMark(-TimelineControl.ruler.SuperNudgeTime);
					break;
				case Keys.Right:
					TimelineControl.ruler.NudgeMark(TimelineControl.ruler.StandardNudgeTime);
					break;
				case (Keys.Right | Keys.Shift):
					TimelineControl.ruler.NudgeMark(TimelineControl.ruler.SuperNudgeTime);
					break;
				//case Keys.Escape:
					//EffectsForm.DeselectAllNodes();
					//toolStripButton_DrawMode.Checked = false;
					//toolStripButton_SelectionMode.Checked = true;
					//break;
			}
			return base.ProcessCmdKey(ref msg, keyData);
		}

		protected override void OnKeyDown(KeyEventArgs e)
		{
			// do anything special we want to here: keyboard shortcuts that are in
			// the menu will be handled by them instead.
			switch (e.KeyCode)
			{
				//case Keys.Delete:
				//	TimelineControl.ruler.DeleteSelectedMarks();
				//	break;
				case Keys.Home:
					if (e.Control)
						TimelineControl.VisibleTimeStart = TimeSpan.Zero;
					else
						TimelineControl.VerticalOffset = 0;
					break;

				case Keys.End:
					if (e.Control)
						TimelineControl.VisibleTimeStart = TimelineControl.TotalTime - TimelineControl.VisibleTimeSpan;
					else
						TimelineControl.VerticalOffset = int.MaxValue; // a bit iffy, but we know that the grid caps it to what's visible
					break;

				case Keys.PageUp:
					if (e.Control)
						TimelineControl.VisibleTimeStart -= TimelineControl.VisibleTimeSpan.Scale(0.5);
					else
						TimelineControl.VerticalOffset -= (TimelineControl.VisibleHeight / 2);
					break;

				case Keys.PageDown:
					if (e.Control)
						TimelineControl.VisibleTimeStart += TimelineControl.VisibleTimeSpan.Scale(0.5);
					else
						TimelineControl.VerticalOffset += (TimelineControl.VisibleHeight / 2);
					break;

				case Keys.Space:
					if (!_context.IsRunning)
						PlaySequence();
					else
					{
						if (_context.IsPaused)
							PlaySequence();
						else
							StopSequence();
					}
					break;

				case Keys.Left:
					if (e.Control)
						TimelineControl.MoveSelectedElementsByTime(TimelineControl.TimePerPixel.Scale(-2));
					break;

				case Keys.Right:
					if (e.Control)
						TimelineControl.MoveSelectedElementsByTime(TimelineControl.TimePerPixel.Scale(2));
					break;

				case Keys.Escape:
					if (TimelineControl.grid._beginEffectDraw) //If we are drawing, prevent escape
						return;
					EffectsForm.DeselectAllNodes();
					TimelineControl.grid.EnableDrawMode = false;
					toolStripButton_DrawMode.Checked = false;
					toolStripButton_SelectionMode.Checked = true;
					break;

				case Keys.OemMinus:
					if (e.Control && e.Shift)
						TimelineControl.ZoomRows(.8);
					else if (e.Control)
						TimelineControl.Zoom(1.25);
					break;

				case Keys.Oemplus:
					if (e.Control && e.Shift)
						TimelineControl.ZoomRows(1.25);
					else if (e.Control)
						TimelineControl.Zoom(.8);
					break;
				case Keys.Z:
					if (e.Control)
					{
						if (_undoMgr.NumUndoable > 0)
						{
							_undoMgr.Undo();	
						}
					}
					break;
				case Keys.Y:
					if (e.Control)
					{
						if (_undoMgr.NumRedoable > 0)
						{
							_undoMgr.Redo();
						}
					}
					break;
			}
			// Prevents sending keystrokes to child controls. 
			// This was causing serious slowdowns if random keys were pressed.
			//e.SuppressKeyPress = true;
			base.OnKeyDown(e);
		}

		protected override void OnFormClosed(FormClosedEventArgs e)
		{
			VixenSystem.Contexts.ReleaseContext(_context);
		}

		#endregion

		#region Clipboard

		private void ClipboardAddData(bool cutElements)
		{
			if (!TimelineControl.SelectedElements.Any())
				return;

			TimelineElementsClipboardData result = new TimelineElementsClipboardData
			{
				FirstVisibleRow = -1,
				EarliestStartTime = TimeSpan.MaxValue,
			};

			int rownum = 0;
			var affectedElements = new List<Element>();
			foreach (Row row in TimelineControl.VisibleRows)
			{
				// Since removals may happen during enumeration, make a copy with ToArray().
				
				affectedElements.AddRange(row.SelectedElements);
				foreach (Element elem in row.SelectedElements.ToArray())
				{
					if (result.FirstVisibleRow == -1)
						result.FirstVisibleRow = rownum;

					int relativeVisibleRow = rownum - result.FirstVisibleRow;

					TimelineElementsClipboardData.EffectModelCandidate modelCandidate =
						new TimelineElementsClipboardData.EffectModelCandidate(elem.EffectNode.Effect)
							{
								Duration = elem.Duration,
								StartTime = elem.StartTime
							};
					result.EffectModelCandidates.Add(modelCandidate, relativeVisibleRow);

					if (elem.StartTime < result.EarliestStartTime)
						result.EarliestStartTime = elem.StartTime;

					if (cutElements)
					{
						row.RemoveElement(elem);
						_sequence.RemoveData(elem.EffectNode);
						SequenceModified();
					}
				}
				rownum++;
			}
			if (cutElements)
			{
				var act = new EffectsCutUndoAction(this, affectedElements.Select(x => x.EffectNode));
				_undoMgr.AddUndoAction(act);	
			}
			

			IDataObject dataObject = new DataObject(ClipboardFormatName);
			dataObject.SetData(result);
			Clipboard.SetDataObject(dataObject, true);
			_TimeLineSequenceClipboardContentsChanged(EventArgs.Empty);
		}

		private void ClipboardCut()
		{
			ClipboardAddData(true);
		}

		private void ClipboardCopy()
		{
			ClipboardAddData(false);
		}

		/// <summary>
		/// Pastes the clipboard data starting at the given time. If pasting to a SelectedRow, the time passed should be TimeSpan.Zero
		/// </summary>
		/// <param name="pasteTime"></param>
		/// <returns></returns>
		public int ClipboardPaste(TimeSpan pasteTime)
		{
			int result = 0;
			TimelineElementsClipboardData data = null;
			IDataObject dataObject = Clipboard.GetDataObject();

			if (dataObject == null)
				return result;

			if (dataObject.GetDataPresent(ClipboardFormatName.Name))
			{
				data = dataObject.GetData(ClipboardFormatName.Name) as TimelineElementsClipboardData;
			}

			if (data == null)
				return result;
			TimeSpan offset = pasteTime == TimeSpan.Zero ? TimeSpan.Zero : data.EarliestStartTime;
			Row targetRow = TimelineControl.SelectedRow ?? TimelineControl.ActiveRow ?? TimelineControl.TopVisibleRow;
			List<Row> visibleRows = new List<Row>(TimelineControl.VisibleRows);
			int topTargetRoxIndex = visibleRows.IndexOf(targetRow);
			List<EffectNode> nodesToAdd = new List<EffectNode>();
			foreach (KeyValuePair<TimelineElementsClipboardData.EffectModelCandidate, int> kvp in data.EffectModelCandidates)
			{
				TimelineElementsClipboardData.EffectModelCandidate effectModelCandidate = kvp.Key;
				int relativeRow = kvp.Value;

				int targetRowIndex = topTargetRoxIndex + relativeRow;
				TimeSpan targetTime = effectModelCandidate.StartTime - offset + pasteTime;
				if (targetTime > TimelineControl.grid.TotalTime)
				{
					continue;
				}
				if (targetTime + effectModelCandidate.Duration > TimelineControl.grid.TotalTime)
				{
					//Shorten to fit.
					effectModelCandidate.Duration = TimelineControl.grid.TotalTime - targetTime;
				}
				if (targetRowIndex >= visibleRows.Count)
					continue;

				//Make a new effect and populate it with the detail data from the clipboard
				var newEffect = ApplicationServices.Get<IEffectModuleInstance>(effectModelCandidate.TypeId);
				newEffect.ModuleData = effectModelCandidate.GetEffectData();
				
				nodesToAdd.Add(CreateEffectNode(newEffect, visibleRows[targetRowIndex], targetTime, effectModelCandidate.Duration));
				result++;
			}

			// put it in the sequence and in the timeline display
			List<TimedSequenceElement> elements = AddEffectNodes(nodesToAdd);
			SequenceModified();

			var act = new EffectsPastedUndoAction(this, elements.Select(x => x.EffectNode));
			_undoMgr.AddUndoAction(act);

			return result;
		}

		#endregion

		#region Menu Bar

		#region Sequence Menu

		private void toolStripMenuItem_Save_Click(object sender, EventArgs e)
		{
			SaveSequence();
		}

		private void toolStripMenuItem_AutoSave_Click(object sender, EventArgs e)
		{
			SetAutoSave();
		}

		private void toolStripMenuItem_SaveAs_Click(object sender, EventArgs e)
		{
			SaveSequence(null, true);
		}

		private void toolStripMenuItem_Close_Click(object sender, EventArgs e)
		{
			Close();
		}

		private void playToolStripMenuItem_Click(object sender, EventArgs e)
		{
			PlaySequence();
		}

		private void pauseToolStripMenuItem_Click(object sender, EventArgs e)
		{
			PauseSequence();
		}

		private void stopToolStripMenuItem_Click(object sender, EventArgs e)
		{
			StopSequence();
		}

		private void toolStripMenuItem_Loop_CheckedChanged(object sender, EventArgs e)
		{
			toolStripButton_Loop.Checked = toolStripMenuItem_Loop.Checked;
			if (toolStripButton_Loop.Checked && delayOffToolStripMenuItem.Checked != true)
			{
				//No way, we're not doing both! Turn off the delay.
				foreach (ToolStripMenuItem item in playOptionsToolStripMenuItem.DropDownItems)
				{
					item.Checked = false;
				}
				delayOffToolStripMenuItem.Checked = true;
				toolStripStatusLabel3.Visible = toolStripStatusLabel_delayPlay.Visible = false;
			}
		}

		private void delayOffToolStripMenuItem_Click(object sender, EventArgs e)
		{
			timerPostponePlay.Interval = 100;
			ClearDelayPlayItemChecks();
			delayOffToolStripMenuItem.Checked = true;
			toolStripStatusLabel3.Visible = toolStripStatusLabel_delayPlay.Visible = false;
			toolStripButton_Play.ToolTipText = @"Play F5";
		}

		private void delay5SecondsToolStripMenuItem_Click(object sender, EventArgs e)
		{
			timerPostponePlay.Interval = 5000;
			ClearDelayPlayItemChecks();
			delay5SecondsToolStripMenuItem.Checked = toolStripStatusLabel3.Visible = toolStripStatusLabel_delayPlay.Visible = true;
			toolStripStatusLabel_delayPlay.Text = @"5 Seconds";
		}

		private void delay10SecondsToolStripMenuItem_Click(object sender, EventArgs e)
		{
			timerPostponePlay.Interval = 10000;
			ClearDelayPlayItemChecks();
			delay10SecondsToolStripMenuItem.Checked = toolStripStatusLabel3.Visible = toolStripStatusLabel_delayPlay.Visible = true;
			toolStripStatusLabel_delayPlay.Text = @"10 Seconds";
		}

		private void delay20SecondsToolStripMenuItem_Click(object sender, EventArgs e)
		{
			timerPostponePlay.Interval = 20000;
			ClearDelayPlayItemChecks();
			delay20SecondsToolStripMenuItem.Checked = toolStripStatusLabel3.Visible = toolStripStatusLabel_delayPlay.Visible = true;
			toolStripStatusLabel_delayPlay.Text = @"20 Seconds";
		}

		private void delay30SecondsToolStripMenuItem_Click(object sender, EventArgs e)
		{
			timerPostponePlay.Interval = 30000;
			ClearDelayPlayItemChecks();
			delay30SecondsToolStripMenuItem.Checked = toolStripStatusLabel3.Visible = toolStripStatusLabel_delayPlay.Visible = true;
			toolStripStatusLabel_delayPlay.Text = @"30 Seconds";
		}

		private void delay60SecondsToolStripMenuItem_Click(object sender, EventArgs e)
		{
			timerPostponePlay.Interval = 60000;
			ClearDelayPlayItemChecks();
			delay60SecondsToolStripMenuItem.Checked = toolStripStatusLabel3.Visible = toolStripStatusLabel_delayPlay.Visible = true;
			toolStripStatusLabel_delayPlay.Text = @"60 Seconds";
		}

		#endregion

		#region Edit Menu

		private void toolStripMenuItem_Cut_Click(object sender, EventArgs e)
		{
			ClipboardCut();
		}

		private void toolStripMenuItem_Copy_Click(object sender, EventArgs e)
		{
			ClipboardCopy();
		}

		private void toolStripMenuItem_Paste_Click(object sender, EventArgs e)
		{
			Row targetRow = TimelineControl.SelectedRow ?? TimelineControl.ActiveRow ?? TimelineControl.TopVisibleRow;
			ClipboardPaste(targetRow.Selected ? TimeSpan.Zero : TimelineControl.CursorPosition);
		}

		private void toolStripMenuItem_deleteElements_Click(object sender, EventArgs e)
		{
			if (TimelineControl.ruler.selectedMarks.Any())
			{
				TimelineControl.ruler.DeleteSelectedMarks();
			}
			else
				RemoveSelectedElements();
			{
				RemoveSelectedElements();
			}
		}

		private void selectAllElementsToolStripMenuItem_Click(object sender, EventArgs e)
		{
			TimelineControl.SelectAllElements();
		}

		private void toolStripMenuItem_EditEffect_Click(object sender, EventArgs e)
		{
			if (TimelineControl.SelectedElements.Any())
			{
				EditElements(TimelineControl.SelectedElements.Cast<TimedSequenceElement>());
			}
		}

		private void toolStripMenuItem_SnapTo_CheckedChanged(object sender, EventArgs e)
		{
			toolStripButton_SnapTo.Checked = toolStripMenuItem_SnapTo.Checked;
			TimelineControl.grid.EnableSnapTo = toolStripMenuItem_SnapTo.Checked;
		}

		// this seems to break the keyboard shortcuts; the key shortcuts don't get enabled again
		// until the menu is dropped down, which is annoying. These really should be enabled/disabled
		// on select of elements, but that's too annoying for now...
		//private void editToolStripMenuItem_DropDownOpened(object sender, EventArgs e)
		//{
		//    toolStripMenuItem_EditEffect.Enabled = TimelineControl.SelectedElements.Any() ;
		//    toolStripMenuItem_Cut.Enabled = TimelineControl.SelectedElements.Any() ;
		//    toolStripMenuItem_Copy.Enabled = TimelineControl.SelectedElements.Any() ;
		//    toolStripMenuItem_Paste.Enabled = _clipboard != null;		//TODO: fix this when clipboard fixed
		//}

		#endregion

		#region View Menu

		private void toolStripMenuItem_zoomTimeIn_Click(object sender, EventArgs e)
		{
			TimelineControl.Zoom(0.8);
		}

		private void toolStripMenuItem_zoomTimeOut_Click(object sender, EventArgs e)
		{
			TimelineControl.Zoom(1.25);
		}

		private void toolStripMenuItem_zoomRowsIn_Click(object sender, EventArgs e)
		{
			TimelineControl.ZoomRows(1.25);
		}

		private void toolStripMenuItem_zoomRowsOut_Click(object sender, EventArgs e)
		{
			TimelineControl.ZoomRows(0.8);
		}

		#endregion

		#region Tools Menu

		private void toolStripMenuItem_removeAudio_Click(object sender, EventArgs e)
		{
			RemoveAudioAssociation();
		}

		private void toolStripMenuItem_associateAudio_Click(object sender, EventArgs e)
		{
			AddAudioAssociation();
		}

		private void toolStripMenuItem_MarkManager_Click(object sender, EventArgs e)
		{
			ShowMarkManager();
		}

		private void modifySequenceLengthToolStripMenuItem_Click(object sender, EventArgs e)
		{
			string oldLength = _sequence.Length.ToString("m\\:ss\\.fff");
			TextDialog prompt = new TextDialog("Enter new sequence length:", "Sequence Length",
																			   oldLength, true);

			do
			{
				if (prompt.ShowDialog() != DialogResult.OK)
					break;

				TimeSpan time;
				bool success = TimeSpan.TryParseExact(prompt.Response, TimeFormats.PositiveFormats, null, out time);
				if (success)
				{
					SequenceLength = time;
					SequenceModified();
					break;
				}
				
				MessageBox.Show(@"Error parsing time: please use the format '<minutes>:<seconds>.<milliseconds>'",
					@"Error parsing time");
			} while (true);
		}

		private void effectWindowToolStripMenuItem_Click(object sender, EventArgs e)
		{
			if (EffectsForm.DockState == DockState.Unknown)
			{
				DockState dockState = EffectsForm.DockState;
				if (dockState == DockState.Unknown) dockState = DockState.DockLeft;
				EffectsForm.Show(dockPanel, dockState);
				//We have to re-subscribe to the event handlers
				EffectsForm.EscapeDrawMode += EscapeDrawMode;
			}
			else
			{
				EffectsForm.Close();
			}
		}

		private void markWindowToolStripMenuItem_Click(object sender, EventArgs e)
		{
			if (MarksForm.DockState == DockState.Unknown)
			{
				DockState dockState = MarksForm.DockState;
				dockState = DockState.DockLeft;
				if (dockState == DockState.Unknown) dockState = DockState.DockLeft;
				MarksForm.Show(dockPanel, dockState);
			}
			else
			{
				MarksForm.Close();
			}
		}

		private void toolWindowToolStripMenuItem_Click(object sender, EventArgs e)
		{
			if (ToolsForm.DockState == DockState.Unknown)
			{
				DockState dockState = ToolsForm.DockState;
				dockState = DockState.DockLeft;
				if (dockState == DockState.Unknown) dockState = DockState.DockLeft;
				ToolsForm.Show(dockPanel, dockState);
				//We have to re-subscribe to the event handlers
				ToolsForm.StartColorDrag += ToolPalette_ColorDrag;
				ToolsForm.StartCurveDrag += ToolPalette_CurveDrag;
				ToolsForm.StartGradientDrag += ToolPalette_GradientDrag;
			}
			else
			{
				ToolsForm.Close();
			}
		}


		#endregion

		#endregion

		#region Toolbar buttons

		private void toolStripButton_Start_Click(object sender, EventArgs e)
		{
			//TODO: JEMA - Check to see if this is functioning properly.
			TimelineControl.PlaybackStartTime = _mPrevPlaybackStart = TimeSpan.Zero;
			TimelineControl.VisibleTimeStart = TimeSpan.Zero;
		}

		private void toolStripButton_Play_Click(object sender, EventArgs e)
		{
			PlaySequence();
		}

		private void toolStripButton_Stop_Click(object sender, EventArgs e)
		{
			StopSequence();
		}

		private void toolStripButton_Pause_Click(object sender, EventArgs e)
		{
			PauseSequence();
		}

		private void toolStripButton_End_Click(object sender, EventArgs e)
		{
			//TODO: JEMA - Check to see if this is functioning properly.
			TimelineControl.PlaybackStartTime = _mPrevPlaybackEnd = _sequence.Length;

			TimelineControl.VisibleTimeStart = TimelineControl.TotalTime - TimelineControl.VisibleTimeSpan;
		}

		private void toolStripButton_Loop_CheckedChanged(object sender, EventArgs e)
		{
			toolStripMenuItem_Loop.Checked = toolStripButton_Loop.Checked;
		}

		private void toolStripButton_SnapTo_CheckedChanged(object sender, EventArgs e)
		{
			toolStripMenuItem_SnapTo.Checked = toolStripButton_SnapTo.Checked;
			TimelineControl.grid.EnableSnapTo = toolStripButton_SnapTo.Checked;
		}

		private void toolStripButton_IncreaseTimingSpeed_Click(object sender, EventArgs e)
		{
			_SetTimingSpeed(_timingSpeed + _timingChangeDelta);
		}

		private void toolStripButton_DecreaseTimingSpeed_Click(object sender, EventArgs e)
		{
			_SetTimingSpeed(_timingSpeed - _timingChangeDelta);
		}

		private void toolStripButtonSnapToStrength_MenuItem_Click(object sender, EventArgs e)
		{

			ToolStripMenuItem item = sender as ToolStripMenuItem;
			if (item != null && !item.Checked)
			{
				foreach (ToolStripMenuItem subItem in item.Owner.Items)
				{
					if (!item.Equals(subItem) && subItem != null)
					{
						subItem.Checked = false;
					}
				}
				item.Checked = true;
				TimelineControl.ruler.SnapStrength = TimelineControl.grid.SnapStrength = Convert.ToInt32(item.Tag);
				PopulateMarkSnapTimes();
				
			} 
			
			// clicking the currently checked one--do not uncheck it
			
		}

		private void toolStripButtonCloseGapStrength_MenuItem_Click(object sender, EventArgs e)
		{
			ToolStripMenuItem item = sender as ToolStripMenuItem;
			if (item != null && !item.Checked)
			{
				foreach (ToolStripMenuItem subItem in item.Owner.Items)
				{
					if (!item.Equals(subItem) && subItem != null)
					{
						subItem.Checked = false;
					}
				}
				item.Checked = true;
				TimelineControl.grid.CloseGap_Threshold = item.Tag.ToString();
			}
		}

		#endregion

		#region Undo

		private void InitUndo()
		{
			_undoMgr = new UndoManager();
			_undoMgr.UndoItemsChanged += _undoMgr_UndoItemsChanged;
			_undoMgr.RedoItemsChanged += _undoMgr_RedoItemsChanged;

			undoButton.Enabled = false;
			undoButton.ItemChosen += undoButton_ItemChosen;

			redoButton.Enabled = false;
			redoButton.ItemChosen += redoButton_ItemChosen;
		}


		private void undoButton_ButtonClick(object sender, EventArgs e)
		{
			_undoMgr.Undo();
		}

		private void undoButton_ItemChosen(object sender, UndoMultipleItemsEventArgs e)
		{
			_undoMgr.Undo(e.NumItems);
		}

		private void redoButton_ButtonClick(object sender, EventArgs e)
		{
			_undoMgr.Redo();
		}

		private void redoButton_ItemChosen(object sender, UndoMultipleItemsEventArgs e)
		{
			_undoMgr.Redo(e.NumItems);
		}


		private void _undoMgr_UndoItemsChanged(object sender, EventArgs e)
		{
			if (_undoMgr.NumUndoable == 0)
			{
				undoButton.Enabled = false;
				return;
			}

			undoButton.Enabled = true;
			undoButton.UndoItems.Clear();
			foreach (var act in _undoMgr.UndoActions)
				undoButton.UndoItems.Add(act.Description);
		}

		private void _undoMgr_RedoItemsChanged(object sender, EventArgs e)
		{
			if (_undoMgr.NumRedoable == 0)
			{
				redoButton.Enabled = false;
				return;
			}

			redoButton.Enabled = true;
			redoButton.UndoItems.Clear();
			foreach (var act in _undoMgr.RedoActions)
				redoButton.UndoItems.Add(act.Description);
		}


		private void timelineControl_ElementsMovedNew(object sender, ElementsChangedTimesEventArgs e)
		{
			var action = new ElementsTimeChangedUndoAction(this, e.PreviousTimes, e.Type);
			_undoMgr.AddUndoAction(action);
		}

		/// <summary>
		/// Used by the Undo/Redo engine
		/// </summary>
		/// <param name="changedElements"></param>
		public void SwapPlaces(Dictionary<Element, ElementTimeInfo> changedElements)
		{
			TimelineControl.grid.SwapElementPlacement(changedElements);
		}

		#endregion

		#region IEditorUserInterface implementation

		public bool IsModified
		{
			get { return _mModified; }
		}

		public void RefreshSequence()
		{
			Sequence = Sequence;
		}

		/// <summary>
		/// Saves the sequence to the optional given path
		/// </summary>
		/// <param name="filePath"></param>
		public void Save(string filePath = null)
		{
			SaveSequence(filePath);
		}

		public ISelection Selection
		{
			get { throw new NotImplementedException(); }
		}

		public ISequence Sequence
		{
			get { return _sequence; }
			set
			{
				if (value is TimedSequence)
				{
					_sequence = (TimedSequence)value;
				}
				else
				{
					throw new NotImplementedException("Cannot use sequence type with a Timed Sequence Editor");
				}
				//loadSequence(value); 
			}
		}

		public IEditorModuleInstance OwnerModule { get; set; }

		void IEditorUserInterface.StartEditor()
		{
			Show();
		}

		void IEditorUserInterface.CloseEditor()
		{
			Close();
		}

		void IEditorUserInterface.EditorClosing()
		{
			
			dockPanel.SaveAsXml(_settingsPath);
			MarksForm.Close();
			EffectsForm.Close();

			var xml = new XMLProfileSettings();
			xml.PutSetting(XMLProfileSettings.SettingType.AppSettings, string.Format("{0}/DockLeftPortion", Name), (int)dockPanel.DockLeftPortion);
			xml.PutSetting(XMLProfileSettings.SettingType.AppSettings, string.Format("{0}/DockRightPortion", Name), (int)dockPanel.DockLeftPortion);
			xml.PutSetting(XMLProfileSettings.SettingType.AppSettings, string.Format("{0}/AutoSaveEnabled", Name), autoSaveToolStripMenuItem.Checked);
			xml.PutSetting(XMLProfileSettings.SettingType.AppSettings, string.Format("{0}/DrawModeSelected", Name), toolStripButton_DrawMode.Checked);
			xml.PutSetting(XMLProfileSettings.SettingType.AppSettings, string.Format("{0}/SelectionModeSelected", Name), toolStripButton_SelectionMode.Checked);
			xml.PutSetting(XMLProfileSettings.SettingType.AppSettings, string.Format("{0}/SnapToSelected", Name), toolStripButton_SnapTo.Checked);
			xml.PutSetting(XMLProfileSettings.SettingType.AppSettings, string.Format("{0}/WindowHeight", Name), Size.Height);
			xml.PutSetting(XMLProfileSettings.SettingType.AppSettings, string.Format("{0}/WindowWidth", Name), Size.Width);
			xml.PutSetting(XMLProfileSettings.SettingType.AppSettings, string.Format("{0}/WindowLocationX", Name), Location.X);
			xml.PutSetting(XMLProfileSettings.SettingType.AppSettings, string.Format("{0}/WindowLocationY", Name), Location.Y);
			xml.PutSetting(XMLProfileSettings.SettingType.AppSettings, string.Format("{0}/WindowState", Name), WindowState.ToString());
			xml.PutSetting(XMLProfileSettings.SettingType.AppSettings, string.Format("{0}/SnapStrength", Name), TimelineControl.grid.SnapStrength);
			xml.PutSetting(XMLProfileSettings.SettingType.AppSettings, string.Format("{0}/CloseGapThreshold", Name), TimelineControl.grid.CloseGap_Threshold);
			xml.PutSetting(XMLProfileSettings.SettingType.AppSettings, string.Format("{0}/ResizeIndicatorEnabled", Name), TimelineControl.grid.ResizeIndicator_Enabled);
			xml.PutSetting(XMLProfileSettings.SettingType.AppSettings, string.Format("{0}/CadStyleSelectionBox", Name), cADStyleSelectionBoxToolStripMenuItem.Checked);
			xml.PutSetting(XMLProfileSettings.SettingType.AppSettings, string.Format("{0}/ResizeIndicatorColor", Name), TimelineControl.grid.ResizeIndicator_Color);
			xml.PutSetting(XMLProfileSettings.SettingType.AppSettings, string.Format("{0}/ToolPaletteLinkCurves", Name), ToolsForm.LinkCurves);
			xml.PutSetting(XMLProfileSettings.SettingType.AppSettings, string.Format("{0}/ToolPaletteLinkGradients", Name), ToolsForm.LinkGradients);

			//This .Close is here because we need to save some of the settings from the form before it is closed.
			ToolsForm.Close();

			//These are only saved in options
			//xml.PutPreference(string.Format("{0}/AutoSaveInterval", Name), _autoSaveTimer.Interval);

			//Clean up any old locations from before we organized the settings.
			xml.RemoveNode("StandardNudge");
			xml.RemoveNode("SuperNudge");
			xml.RemoveNode(Name);

		}

		#endregion

		#region IExecutionControl and ITiming implementation - beat tapping

		void IExecutionControl.Resume()
		{
			PlaySequence();
		}

		void IExecutionControl.Start()
		{
			PlaySequence();
		}

		void IExecutionControl.Pause()
		{
			PauseSequence();
		}

		void IExecutionControl.Stop()
		{
			StopSequence();
		}

		TimeSpan ITiming.Position
		{
			get { return TimingSource.Position; }
			set { }
		}

		public bool SupportsVariableSpeeds
		{
			get { return false; }
		}

		public float Speed
		{
			get { return _timingSpeed; }
			set { _SetTimingSpeed(value); }
		}

		public bool PositionHasValue
		{
			get { return TimelineControl.PlaybackCurrentTime.HasValue; }
		}

		#endregion

		//*** only do this if the user agrees to do it
		private void _UpdateTimingSourceToSelectedMedia()
		{
			//This sucks so bad, I am so sorry.  Magic strings and everything, good god.
			TimingProviders timingProviders = new TimingProviders(_sequence);
			string[] mediaTimingSources;

			try
			{
				mediaTimingSources = timingProviders.GetAvailableTimingSources("Media");
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.Message);
				return;
			}

			if (mediaTimingSources.Length > 0)
			{
				SelectedTimingProvider mediaTimingProvider = new SelectedTimingProvider("Media", mediaTimingSources.First());
				_sequence.SelectedTimingProvider = mediaTimingProvider;
				_SetTimingToolStripEnabledState();
			}
		}

		private void ShowMarkManager()
		{
			MarkManager manager = new MarkManager(new List<MarkCollection>(_sequence.MarkCollections), this, this, this);
			if (manager.ShowDialog() == DialogResult.OK)
			{
				_sequence.MarkCollections = manager.MarkCollections;
				PopulateMarkSnapTimes();
				SequenceModified();
				MarksForm.PopulateMarkCollectionsList(null);
			}
		}
	
		private void _SetTimingSpeed(float speed)
		{
			if (speed <= 0) throw new InvalidOperationException("Cannot have a speed of 0 or less.");

			_timingSpeed = speed;

			// If they haven't executed the sequence yet, the timing source member will not yet be set.
			if (TimingSource != null)
			{
				TimingSource.Speed = _timingSpeed;
			}

			_UpdateTimingSpeedDisplay();
			toolStripButton_DecreaseTimingSpeed.Enabled = _timingSpeed > _timingChangeDelta;
		}

		private void _UpdateTimingSpeedDisplay()
		{
			toolStripLabel_TimingSpeed.Text = _timingSpeed.ToString("p0");
		}

		private void _SetTimingToolStripEnabledState()
		{
			if (InvokeRequired)
				Invoke(new Delegates.GenericDelegate(_SetTimingToolStripEnabledState));
			else
			{
				ITiming timingSource = _sequence.GetTiming();
				toolStripButton_IncreaseTimingSpeed.Enabled =
					toolStripButton_DecreaseTimingSpeed.Enabled =
					toolStripLabel_TimingSpeed.Enabled = toolStripLabel_TimingSpeedLabel.Enabled =
				   timingSource != null && timingSource.SupportsVariableSpeeds;

			}
		}

		private void _PlaySequence(TimeSpan rangeStart, TimeSpan rangeEnd)
		{
			if (_context.IsRunning && _context.IsPaused)
			{
				_context.Resume();
				UpdateButtonStates();
			}
			else
			{
				if (toolStripButton_Loop.Checked)
				{
					_context.PlayLoop(rangeStart, rangeEnd);
				}
				else
				{
					_context.Play(rangeStart, rangeEnd);	
				}
				
			}

			//_SetTimingSpeed(_timingSpeed);
		}

		private ITiming TimingSource
		{
			get { return _timingSource; }
			set
			{
				_timingSource = value;

				if (value == null) return;

				if (_timingSource.SupportsVariableSpeeds)
				{
					_timingSource.Speed = _timingSpeed;
					toolStripButton_IncreaseTimingSpeed.Enabled =
							toolStripLabel_TimingSpeed.Enabled = toolStripLabel_TimingSpeedLabel.Enabled = true;
					toolStripButton_DecreaseTimingSpeed.Enabled = toolStripButton_DecreaseTimingSpeed.Enabled = _timingSpeed > _timingChangeDelta;

				}
				else
				{
					_UpdateTimingSpeedDisplay();
					toolStripButton_IncreaseTimingSpeed.Enabled =
				   toolStripButton_DecreaseTimingSpeed.Enabled =
				   toolStripLabel_TimingSpeed.Enabled = toolStripLabel_TimingSpeedLabel.Enabled = false;
				}
			}
		}

		private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
		private readonly Task _loadingTask = null;

		private void TimedSequenceEditorForm_Shown(object sender, EventArgs e)
		{
			Enabled = false;
			FormBorderStyle = FormBorderStyle.FixedSingle;
			//loadingTask = Task.Factory.StartNew(() => loadSequence(_sequence), token);
			LoadSequence(_sequence);
		}

		private void cboAudioDevices_TextChanged(object sender, EventArgs e)
		{
			Variables.SelectedAudioDeviceIndex = cboAudioDevices.SelectedIndex;
		}

		private void cboAudioDevices_SelectedIndexChanged(object sender, EventArgs e)
		{
			Variables.SelectedAudioDeviceIndex = cboAudioDevices.SelectedIndex;
		}

		private void menuStrip_MenuActivate(object sender, EventArgs e)
		{
			effectWindowToolStripMenuItem.Checked = (EffectsForm.DockState != DockState.Unknown);
			markWindowToolStripMenuItem.Checked = (MarksForm.DockState != DockState.Unknown);
			toolWindowToolStripMenuItem.Checked = (ToolsForm.DockState != DockState.Unknown);
		}

		private void timerPostponePlay_Tick(object sender, EventArgs e)
		{
			timerPostponePlay.Enabled = timerDelayCountdown.Enabled = false;
			PlaySequence();
		}

		private void ClearDelayPlayItemChecks()
		{
			//Make sure Looping is not enabled
			toolStripButton_Loop.Checked = toolStripMenuItem_Loop.Checked = false;
			foreach (ToolStripMenuItem item in playOptionsToolStripMenuItem.DropDownItems)
			{
				item.Checked = false;
			}
		}

		private void timerDelayCountdown_Tick(object sender, EventArgs e)
		{
			_delayCountDown--;
			toolStripStatusLabel_delayPlay.Text = string.Format("{0} Seconds", _delayCountDown);
		}

		private void curveEditorToolStripMenuItem_Click(object sender, EventArgs e)
		{
			var selector = new CurveLibrarySelector{DoubleClickMode = CurveLibrarySelector.Mode.Edit};
			selector.ShowDialog();
		}

		private void colorGradientToolStripMenuItem_Click(object sender, EventArgs e)
		{
			var selector = new ColorGradientLibrarySelector{DoubleClickMode = ColorGradientLibrarySelector.Mode.Edit};
			selector.ShowDialog();
		}

        private void editMapsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            LipSyncMapSelector mapSelector = new LipSyncMapSelector();
            DialogResult dr = mapSelector.ShowDialog();
            if (mapSelector.Changed)
            {
                mapSelector.Changed = false;
                SequenceModified();
                ResetLipSyncNodes();
                VixenSystem.SaveSystemConfig();
	        }
        }

        private void setDefaultMap_Click(object sender,EventArgs e)
        {
            ToolStripMenuItem menu = (ToolStripMenuItem)sender;
            if (!_library.DefaultMappingName.Equals(menu.Text))
            {
                _library.DefaultMappingName = menu.Text; 
                SequenceModified();
            }
            
        }

        private void ResetLipSyncNodes()
        {
            foreach (Row row in TimelineControl.Rows)
            {
                for (int j = 0; j < row.ElementCount; j++)
                {
                    Element elem = row.ElementAt(j);
					IEffectModuleInstance effect = elem.EffectNode.Effect;
					if (effect.GetType() == typeof(LipSync))
					{
						((LipSync)effect).MakeDirty();
					}

                }
            }

            TimelineControl.grid.RenderAllRows();
        }

        private void defaultMapToolStripMenuItem_DropDownOpening(object sender, EventArgs e)
        {
            string defaultText = _library.DefaultMappingName;
            defaultMapToolStripMenuItem.DropDownItems.Clear();
            
            foreach (LipSyncMapData mapping in _library.Library.Values)
            {
                ToolStripMenuItem menuItem = new ToolStripMenuItem(mapping.LibraryReferenceName);
                menuItem.Click += setDefaultMap_Click;
                menuItem.Checked = _library.IsDefaultMapping(mapping.LibraryReferenceName);
                defaultMapToolStripMenuItem.DropDownItems.Add(menuItem);
            }            
        }

        private void papagayoImportToolStripMenuItem_Click(object sender, EventArgs e)
        {
	        PapagayoDoc papagayoFile = new PapagayoDoc();
            FileDialog openDialog = new OpenFileDialog();

            openDialog.Filter = @"Papagayo files (*.pgo)|*.pgo|All files (*.*)|*.*";
            openDialog.FilterIndex = 1;
            if (openDialog.ShowDialog() != DialogResult.OK)
            {
                return;
            }
            string fileName = openDialog.FileName;
            papagayoFile.Load(fileName);

            TimelineElementsClipboardData result = new TimelineElementsClipboardData
            {
                FirstVisibleRow = -1,
                EarliestStartTime = TimeSpan.MaxValue,
            };

            result.FirstVisibleRow = 0;

            int rownum = 0;
            foreach (string voice in papagayoFile.VoiceList)
            {
                List<PapagayoPhoneme> phonemes = papagayoFile.PhonemeList(voice);

                if (phonemes.Count > 0)
                {

                    foreach (PapagayoPhoneme phoneme in phonemes)
                    {
                        if (phoneme.DurationMS == 0.0)
                        {
                            continue;
                        }

                        IEffectModuleInstance effect =
                            ApplicationServices.Get<IEffectModuleInstance>(new LipSyncDescriptor().TypeId);

                        ((LipSync)effect).StaticPhoneme = phoneme.TypeName.ToUpper();
                        ((LipSync)effect).LyricData = phoneme.LyricData;

                        TimeSpan startTime = TimeSpan.FromMilliseconds(phoneme.StartMS);
                        TimelineElementsClipboardData.EffectModelCandidate modelCandidate =
                              new TimelineElementsClipboardData.EffectModelCandidate(effect)
                              {
                                  Duration = TimeSpan.FromMilliseconds(phoneme.DurationMS - 1),
                                  StartTime = startTime,
                              };

                        result.EffectModelCandidates.Add(modelCandidate, rownum);
                        if (startTime < result.EarliestStartTime)
                            result.EarliestStartTime = startTime;

                        effect.Render();

                    }
                    
                    IDataObject dataObject = new DataObject(ClipboardFormatName);
                    dataObject.SetData(result);
                    Clipboard.SetDataObject(dataObject, true);
                    _TimeLineSequenceClipboardContentsChanged(EventArgs.Empty);
                    SequenceModified();

                }
                rownum++;
            }
            
            string displayStr = rownum + " Voices imported to clipboard as seperate rows\n\n";
            
            int j = 1;
            foreach (string voiceStr in papagayoFile.VoiceList)
            {
                displayStr += "Row #" + j +" - " + voiceStr + "\n";
                j++;
            }
            
            MessageBox.Show(displayStr, @"Papagayo Import", MessageBoxButtons.OK);
        }

        private void textConverterHandler(object sender, NewTranslationEventArgs args)
        {
            TimelineElementsClipboardData result = new TimelineElementsClipboardData
            {
                FirstVisibleRow = -1,
                EarliestStartTime = TimeSpan.MaxValue,
            };

            if (args.PhonemeData.Count > 0)
            {

                foreach (LipSyncConvertData data in args.PhonemeData)
                {
                    if (data.Duration.Ticks == 0)
                    {
                        continue;
                    }

                    IEffectModuleInstance effect =
                        ApplicationServices.Get<IEffectModuleInstance>(new LipSyncDescriptor().TypeId);

                    ((LipSync)effect).StaticPhoneme = data.Phoneme.ToString().ToUpper();
                    ((LipSync)effect).LyricData = data.LyricData;

                    TimelineElementsClipboardData.EffectModelCandidate modelCandidate =
                          new TimelineElementsClipboardData.EffectModelCandidate(effect)
                          {
                              Duration = data.Duration,
                              StartTime = data.StartOffset
                          };

                    result.EffectModelCandidates.Add(modelCandidate, 0);
                    if (data.StartOffset < result.EarliestStartTime)
                        result.EarliestStartTime = data.StartOffset;

                    effect.Render();

                }

                IDataObject dataObject = new DataObject(ClipboardFormatName);
                dataObject.SetData(result);
                Clipboard.SetDataObject(dataObject, true);
                _TimeLineSequenceClipboardContentsChanged(EventArgs.Empty);

                int pasted = 0;

                if (args.Placement == TranslatePlacement.Cursor)
                {
                    args.FirstMark += TimelineControl.grid.CursorPosition;
                }

                if (args.Placement != TranslatePlacement.Clipboard)
                {
                    pasted = ClipboardPaste(args.FirstMark);
                }

                if (pasted == 0)
                {
                    MessageBox.Show(@"Conversion Complete and copied to Clipboard \n Paste at first Mark offset", @"Convert Text", MessageBoxButtons.OK);
                }

                SequenceModified();

            }
        }

        private void translateFailureHandler(object sender, TranslateFailureEventArgs args)
        {
            LipSyncTextConvertFailForm failForm = new LipSyncTextConvertFailForm
            {
	            errorLabel =
	            {
		            Text = @"Unable to find mapping for " + args.FailedWord + Environment.NewLine +
		                   @"Please map using buttons below"
	            }
            };
	        DialogResult dr = failForm.ShowDialog();
            if (dr == DialogResult.OK)
            {
                LipSyncTextConvert.AddUserMaping(args.FailedWord + " " + failForm.TranslatedString);
            }
        }

        private void textConverterToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (LipSyncTextConvertForm.Active == false)
            {
                LipSyncTextConvertForm textConverter = new LipSyncTextConvertForm();
                textConverter.NewTranslation += textConverterHandler;
                textConverter.TranslateFailure += translateFailureHandler;
                textConverter.MarkCollections = _sequence.MarkCollections;
                textConverter.Show(this);
            }
        }

        private void lipSyncMappingsToolStripMenuItem_DropDownOpening(object sender, EventArgs e)
        {
            changeMapToolStripMenuItem.Enabled =
             (_library.Library.Count > 1) &&
             (TimelineControl.SelectedElements.Any(effect => effect.EffectNode.Effect.GetType() == typeof(LipSync)));
        }


        private void changeMapToolStripMenuItem_DropDownOpening(object sender, EventArgs e)
        {
            string defaultText = _library.DefaultMappingName;
            changeMapToolStripMenuItem.DropDownItems.Clear();

            foreach (LipSyncMapData mapping in _library.Library.Values)
            {
                ToolStripMenuItem menuItem = new ToolStripMenuItem(mapping.LibraryReferenceName);
                menuItem.Click += changeMappings_Click;
                changeMapToolStripMenuItem.DropDownItems.Add(menuItem);
            }
        }

        private void changeMappings_Click(object sender, EventArgs e)
        {
            ToolStripMenuItem toolStripSender = (ToolStripMenuItem)sender;

            TimelineControl.SelectedElements.ToList().ForEach(delegate(Element element)
            {
                if (element.EffectNode.Effect.GetType() == typeof(LipSync))
                {
                    ((LipSync)element.EffectNode.Effect).PhonemeMapping =  toolStripSender.Text;
                    ResetLipSyncNodes();
                }
            });

        }

		private void helpDocumentationToolStripMenuItem_Click(object sender, EventArgs e)
		{
			Process.Start("http://www.vixenlights.com/vixen-3-documentation/sequencer/");
		}
		private void exportToolStripMenuItem_Click(object sender, EventArgs e)
		{
            ExportDialog ed = new ExportDialog(Sequence);
            ed.ShowDialog();
		}

		private void bulkEffectMoveToolStripMenuItem_Click(object sender, EventArgs e)
		{

			var dialog = new BulkEffectMoveForm(TimelineControl.grid.CursorPosition);
			using (dialog)
			{
				if (dialog.ShowDialog() == DialogResult.OK)
				{
					TimelineControl.grid.MoveElementsInRangeByTime(dialog.Start, dialog.End, dialog.IsForward?dialog.Offset:-dialog.Offset);
				}
			}
		}

		private void cADStyleSelectionBoxToolStripMenuItem_Click(object sender, EventArgs e)
		{
			TimelineControl.grid.aCadStyleSelectionBox = cADStyleSelectionBoxToolStripMenuItem.Checked;
		}

		private void toolStripSplitButton_CloseGaps_ButtonClick(object sender, EventArgs e)
		{
			TimelineControl.grid.CloseGapsBetweenElements();
		}


		/// <summary>
		/// Aligns selected elements, or if none, all elements to the closest mark.
		/// alignStart = true to align the start of the elements, false to align the end of the elements.
		/// </summary>
		/// <param name="alignStart"></param>
		private void AlignEffectsToNearestMarks(bool alignStart)
		{
			if (!TimelineControl.grid.SelectedElements.Any())
			{
				var result = MessageBox.Show(@"This action will apply to your entire sequence, are you sure ?",
					@"Align effects to marks", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
				if (result == DialogResult.No) return;
			}

			Dictionary<Element, Tuple<TimeSpan, TimeSpan>> moveElements = new Dictionary<Element, Tuple<TimeSpan, TimeSpan>>();

			foreach (Row row in TimelineControl.Rows)
			{
				List<Element> elements = new List<Element>();

				elements = TimelineControl.SelectedElements.Any() ? row.SelectedElements.ToList() : row.ToList();

				if (!elements.Any()) continue;

				foreach (Element element in elements)
				{
					var nearestMark = FindNearestMark(alignStart ? element.StartTime : element.EndTime);
					if (nearestMark != TimeSpan.Zero && !moveElements.ContainsKey(element))
					{
						moveElements.Add(element, new Tuple<TimeSpan, TimeSpan>(alignStart ? nearestMark : element.StartTime, alignStart ? element.EndTime : nearestMark));
					}
				}
			}

			//Make sure we have elements in the list to move.
			if (moveElements.Any()) TimelineControl.grid.MoveResizeElements(moveElements);
		}

		/// <summary>
		/// Returns the TimeSpan location of the nearest mark to the given TimeSpan
		/// Located within the threshhold: TimelineControl.grid.CloseGap_Threshold
		/// </summary>
		/// <param name="referenceTimeSpan"></param>
		/// <returns></returns>
		private TimeSpan FindNearestMark(TimeSpan referenceTimeSpan)
		{
			List<TimeSpan> marksInRange = new List<TimeSpan>();
			var threshold = TimeSpan.FromSeconds(Convert.ToDouble(TimelineControl.grid.CloseGap_Threshold));
			TimeSpan result = TimeSpan.Zero;
			TimeSpan compareResult = TimeSpan.Zero;

			foreach (TimeSpan markTime in _sequence.MarkCollections.SelectMany(markCollection => markCollection.Marks))
			{
				if (markTime == referenceTimeSpan)
				{
					return markTime; //That was easy
				}

				if (markTime > referenceTimeSpan - threshold && markTime < referenceTimeSpan + threshold)
				{
					marksInRange.Add(markTime);
				}
			}

			foreach (TimeSpan markTime in marksInRange)
			{
				if (markTime > referenceTimeSpan && markTime - referenceTimeSpan < compareResult || result == TimeSpan.Zero)
				{
					compareResult = markTime - referenceTimeSpan;
					result = markTime;
				}

				if (markTime < referenceTimeSpan && referenceTimeSpan - markTime < compareResult || result == TimeSpan.Zero)
				{
					compareResult = referenceTimeSpan - markTime;
					result = markTime;
				}
			}

			return result;
		}

		private void toolStripMenuItem_BeatBarDetection_Click(object sender, EventArgs e)
		{
			foreach (IMediaModuleInstance module in _sequence.GetAllMedia())
			{
				if (module is Audio)
				{
					BeatsAndBars audioFeatures = new BeatsAndBars((Audio)module);
					_sequence.MarkCollections = 
						audioFeatures.DoBeatBarDetection(_sequence.MarkCollections);



					MarksForm.PopulateMarkCollectionsList(null);
					SequenceModified();
					break;

				}
			}
		}

	}

	[Serializable]
	internal class TimelineElementsClipboardData
	{
		public TimelineElementsClipboardData()
		{
			EffectModelCandidates = new Dictionary<EffectModelCandidate, int>();
		}

		// a collection of elements and the number of rows they were below the top visible element when
		// this data was generated and placed on the clipboard.
		public Dictionary<EffectModelCandidate, int> EffectModelCandidates { get; set; }

		public int FirstVisibleRow { get; set; }

		public TimeSpan EarliestStartTime { get; set; }

		/// <summary>
		/// Class to hold effect data to allow it to be placed on the clipboard and be reconstructed when later pasted
		/// </summary>
		[Serializable]
		public class EffectModelCandidate
		{
			private readonly Type _moduleDataClass;
			private readonly MemoryStream _effectData;

			public EffectModelCandidate(IEffectModuleInstance effect)
			{
				_moduleDataClass = effect.Descriptor.ModuleDataClass;
				DataContractSerializer ds = new DataContractSerializer(_moduleDataClass);

				TypeId = effect.Descriptor.TypeId;
				_effectData = new MemoryStream();
				using (XmlDictionaryWriter w = XmlDictionaryWriter.CreateBinaryWriter(_effectData))
					ds.WriteObject(w, effect.ModuleData);
			}

			public TimeSpan StartTime { get; set; }
			public TimeSpan Duration { get; set; }
			public Guid TypeId { get; private set; }

			public IModuleDataModel GetEffectData()
			{
				DataContractSerializer ds = new DataContractSerializer(_moduleDataClass);
				MemoryStream effectDataIn = new MemoryStream(_effectData.ToArray());
				using (XmlDictionaryReader r = XmlDictionaryReader.CreateBinaryReader(effectDataIn, XmlDictionaryReaderQuotas.Max))
					return (IModuleDataModel) ds.ReadObject(r);
			}
		}
	}

	public class TimeFormats
	{
		private static readonly string[] _positiveFormats =
		{
			@"m\:ss", @"m\:ss\.f", @"m\:ss\.ff", @"m\:ss\.fff",
			@"\:ss", @"\:ss\.f", @"\:ss\.ff", @"\:ss\.fff",
			@"%s", @"s\.f", @"s\.ff", @"s\.fff"
		};

		private static readonly string[] _negativeFormats =
		{
			@"\-m\:ss", @"\-m\:ss\.f", @"\-m\:ss\.ff", @"\-m\:ss\.fff",
			@"\-\:ss", @"\-\:ss\.f", @"\-\:ss\.ff", @"\-\:ss\.fff",
			@"\-%s", @"\-s\.f", @"\-s\.ff", @"\-s\.fff"
		};

		public static string[] AllFormats
		{
			get { return _negativeFormats.Concat(_positiveFormats).ToArray(); }
		}

		public static string[] PositiveFormats
		{
			get { return _positiveFormats; }
		}

		public static string[] NegativeFormats
		{
			get { return _negativeFormats; }
		}
	}


}