﻿// Copyright (c) AlphaSierraPapa for the SharpDevelop Team (for details please see \doc\copyright.txt)
// This code is distributed under the GNU LGPL (for details please see \doc\license.txt)

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Xml;
using ICSharpCode.WpfDesign.Designer.Xaml;
using ICSharpCode.WpfDesign.Designer.Services;
using ICSharpCode.WpfDesign.Adorners;
using ICSharpCode.WpfDesign.Designer.Controls;
using System.Diagnostics;
using ICSharpCode.WpfDesign.XamlDom;
using System.Threading;
using System.Globalization;

namespace ICSharpCode.WpfDesign.Designer
{
	/// <summary>
	/// Surface hosting the WPF designer.
	/// </summary>
	[TemplatePart(Name = "PART_DesignContent", Type = typeof(ContentControl))]
	[TemplatePart(Name = "PART_Zoom", Type = typeof(ZoomControl))]
	public class DesignSurface : ContentControl
	{
		private FocusNavigator _focusNav;
		
		static DesignSurface()
		{
			DefaultStyleKeyProperty.OverrideMetadata(typeof(DesignSurface), new FrameworkPropertyMetadata(typeof(DesignSurface)));
		}
		
		public DesignSurface()
		{
			//TODO: this is for converters (see PropertyGrid)
			Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

			this.AddCommandHandler(ApplicationCommands.Undo, Undo, CanUndo);
			this.AddCommandHandler(ApplicationCommands.Redo, Redo, CanRedo);
			this.AddCommandHandler(ApplicationCommands.Copy, Copy, CanCopyOrCut);
			this.AddCommandHandler(ApplicationCommands.Cut, Cut, CanCopyOrCut);
			this.AddCommandHandler(ApplicationCommands.Delete, Delete, CanDelete);
			this.AddCommandHandler(ApplicationCommands.Paste, Paste, CanPaste);
			this.AddCommandHandler(ApplicationCommands.SelectAll, SelectAll, CanSelectAll);
			
			_sceneContainer = new Border() { AllowDrop = false, UseLayoutRounding = true };
			_sceneContainer.SetValue(TextOptions.TextFormattingModeProperty, TextFormattingMode.Ideal);

			_designPanel = new DesignPanel() {Child = _sceneContainer};
		}

		internal DesignPanel _designPanel;
		private ContentControl _partDesignContent;
		private Border _sceneContainer;

		public override void OnApplyTemplate()
		{
			_partDesignContent = this.Template.FindName("PART_DesignContent", this) as ContentControl;
			_partDesignContent.Content = _designPanel;
			_partDesignContent.RequestBringIntoView += _partDesignContent_RequestBringIntoView;

			this.ZoomControl = this.Template.FindName("PART_Zoom", this) as ZoomControl;
			
			base.OnApplyTemplate();
		}

		void _partDesignContent_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
		{
			e.Handled = true;
		}

		protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
		{
			if (ZoomControl != null && e.OriginalSource == ZoomControl)
			{
				UnselectAll();
			}
		}

		public ZoomControl ZoomControl { get; private set; }
		
		DesignContext _designContext;

		/// <summary>
		/// Gets the active design context.
		/// </summary>
		public DesignContext DesignContext {
			get { return _designContext; }
		}
		
		/// <summary>
		/// Gets the DesignPanel
		/// </summary>
		public DesignPanel DesignPanel {
			get { return _designPanel; }
		}
		
		/// <summary>
		/// Initializes the designer content from the specified XmlReader.
		/// </summary>
		public void LoadDesigner(XmlReader xamlReader, XamlLoadSettings loadSettings)
		{
			UnloadDesigner();
			loadSettings = loadSettings ?? new XamlLoadSettings();
			loadSettings.CustomServiceRegisterFunctions.Add(
				context => context.Services.AddService(typeof(IDesignPanel), _designPanel));
			InitializeDesigner(new XamlDesignContext(xamlReader, loadSettings));
		}
		
		/// <summary>
		/// Saves the designer content into the specified XmlWriter.
		/// </summary>
		public void SaveDesigner(XmlWriter writer)
		{
			_designContext.Save(writer);
		}
		
		void InitializeDesigner(DesignContext context)
		{
			_designContext = context;
			_designPanel.Context = context;

			if (context.RootItem != null) {
				_sceneContainer.Child = context.RootItem.View;
			}
			
			context.Services.RunWhenAvailable<UndoService>(
				undoService => undoService.UndoStackChanged += delegate {
					CommandManager.InvalidateRequerySuggested();
				}
			);
			context.Services.Selection.SelectionChanged += delegate {
				CommandManager.InvalidateRequerySuggested();
			};
			
			context.Services.AddService(typeof(IKeyBindingService), new DesignerKeyBindings(this));
			_focusNav=new FocusNavigator(this);
			_focusNav.Start();
		}
		
		/// <summary>
		/// Unloads the designer content.
		/// </summary>
		public void UnloadDesigner()
		{
			if (_designContext != null) {
				foreach (object o in _designContext.Services.AllServices) {
					IDisposable d = o as IDisposable;
					if (d != null) d.Dispose();
				}
			}
			_designContext = null;
			_designPanel.Context = null;
			_sceneContainer.Child = null;
			_designPanel.Adorners.Clear();
		}

		#region Commands

		public bool CanUndo()
		{
			UndoService undoService = GetService<UndoService>();
			return undoService != null && undoService.CanUndo;
		}

		public void Undo()
		{
			UndoService undoService = GetService<UndoService>();
			IUndoAction action = undoService.UndoActions.First();
			Debug.WriteLine("Undo " + action.Title);
			undoService.Undo();
			_designContext.Services.Selection.SetSelectedComponents(GetLiveElements(action.AffectedElements));
		}

		public bool CanRedo()
		{
			UndoService undoService = GetService<UndoService>();
			return undoService != null && undoService.CanRedo;
		}

		public void Redo()
		{
			UndoService undoService = GetService<UndoService>();
			IUndoAction action = undoService.RedoActions.First();
			Debug.WriteLine("Redo " + action.Title);
			undoService.Redo();
			_designContext.Services.Selection.SetSelectedComponents(GetLiveElements(action.AffectedElements));
		}

		public bool CanCopyOrCut()
		{
			ISelectionService selectionService = GetService<ISelectionService>();
			if(selectionService!=null){
				if (selectionService.SelectedItems.Count == 0)
					return false;
				if (selectionService.SelectedItems.Count == 1 && selectionService.PrimarySelection == DesignContext.RootItem)
					return false;
			}
			return true;
		}

		public void Copy()
		{
			XamlDesignContext xamlContext = _designContext as XamlDesignContext;
			ISelectionService selectionService = GetService<ISelectionService>();
			if(xamlContext != null && selectionService != null){
				xamlContext.XamlEditAction.Copy(selectionService.SelectedItems);
			}
		}

		public void Cut()
		{
			XamlDesignContext xamlContext = _designContext as XamlDesignContext;
			ISelectionService selectionService = GetService<ISelectionService>();
			if(xamlContext != null && selectionService != null){
				xamlContext.XamlEditAction.Cut(selectionService.SelectedItems);
			}
		}

		public bool CanDelete()
		{
			if (_designContext != null) {
				return ModelTools.CanDeleteComponents(_designContext.Services.Selection.SelectedItems);
			}
			return false;
		}

		public void Delete()
		{
			if (_designContext != null) {
				ModelTools.DeleteComponents(_designContext.Services.Selection.SelectedItems);
			}
		}

		public bool CanPaste()
		{
			ISelectionService selectionService = GetService<ISelectionService>();
			if(selectionService!=null && selectionService.SelectedItems.Count!=0){
				string xaml = Clipboard.GetText(TextDataFormat.Xaml);
				if(xaml != "" && xaml != " ")
					return true;
			}
			return false;
		}

		public void Paste()
		{
			XamlDesignContext xamlContext = _designContext as XamlDesignContext;
			if(xamlContext != null){
				xamlContext.XamlEditAction.Paste();
			}
		}

		public bool CanSelectAll()
		{
			return DesignContext != null;
		}

		//TODO: Do not select layout root
		public void SelectAll()
		{
			var items = Descendants(DesignContext.RootItem).Where(item => ModelTools.CanSelectComponent(item)).ToArray();
			DesignContext.Services.Selection.SetSelectedComponents(items);
		}

		public void UnselectAll()
		{
			DesignContext.Services.Selection.SetSelectedComponents(null);
		}

		//TODO: Share with Outline / PlacementBehavior
		public static IEnumerable<DesignItem> DescendantsAndSelf(DesignItem item)
		{
			yield return item;
			foreach (var child in Descendants(item)) {
				yield return child;
			}
		}

		public static IEnumerable<DesignItem> Descendants(DesignItem item)
		{
			if (item.ContentPropertyName != null) {
				var content = item.ContentProperty;
				if (content.IsCollection) {
					foreach (var child in content.CollectionElements) {
						foreach (var child2 in DescendantsAndSelf(child)) {
							yield return child2;
						}
					}
				} else {
					if (content.Value != null) {
						foreach (var child2 in DescendantsAndSelf(content.Value)) {
							yield return child2;
						}
					}
				}
			}
		}

		// Filters an element list, dropping all elements that are not part of the xaml document
		// (e.g. because they were deleted).
		static List<DesignItem> GetLiveElements(ICollection<DesignItem> items)
		{
			List<DesignItem> result = new List<DesignItem>(items.Count);
			foreach (DesignItem item in items) {
				if (ModelTools.IsInDocument(item) && ModelTools.CanSelectComponent(item)) {
					result.Add(item);
				}
			}
			return result;
		}

		T GetService<T>() where T : class
		{
			if (_designContext != null)
				return _designContext.Services.GetService<T>();
			else
				return null;
		}

		#endregion
	}
}
