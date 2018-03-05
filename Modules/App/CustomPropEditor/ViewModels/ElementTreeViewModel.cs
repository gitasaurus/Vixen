﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Catel.Data;
using Catel.MVVM;
using Catel.Services;
using GongSolutions.Wpf.DragDrop;
using GongSolutions.Wpf.DragDrop.Utilities;
using VixenModules.App.CustomPropEditor.Converters;
using VixenModules.App.CustomPropEditor.Model;
using VixenModules.App.CustomPropEditor.Services;

namespace VixenModules.App.CustomPropEditor.ViewModels
{
	public sealed class ElementTreeViewModel : ViewModelBase,  IDropTarget, IDragSource, IDisposable
	{
		public ElementTreeViewModel(Prop prop)
		{
			Prop = prop;
			ElementModelViewModel vm = new ElementModelViewModel(Prop.RootNode, null);
			RootNodesViewModels = new ObservableCollection<ElementModelViewModel>(new[] { vm });
			SelectedItems = new ObservableCollection<ElementModelViewModel>();
			SelectedItems.CollectionChanged += SelectedItems_CollectionChanged;
		}


		#region Prop model property

		/// <summary>
		/// Gets or sets the Prop value.
		/// </summary>
		[Model]
		public Prop Prop
		{
			get { return GetValue<Prop>(PropProperty); }
			private set { SetValue(PropProperty, value); }
		}

		/// <summary>
		/// Prop property data.
		/// </summary>
		public static readonly PropertyData PropProperty = RegisterProperty("Prop", typeof(Prop));

		#endregion

		#region RootNodes property

		/// <summary>
		/// Gets or sets the RootNodes value.
		/// </summary>
		[ViewModelToModel("Prop", "RootNode", ConverterType = typeof(RootNodeToCollectionMapping))]
		public ObservableCollection<ElementModel> RootNodes
		{
			get { return GetValue<ObservableCollection<ElementModel>>(RootNodesProperty); }
			set { SetValue(RootNodesProperty, value); }
		}

		/// <summary>
		/// RootNodes property data.
		/// </summary>
		public static readonly PropertyData RootNodesProperty = RegisterProperty("RootNodes", typeof(ObservableCollection<ElementModel>), null);

		#endregion

		#region RootNodesViewModels property

		/// <summary>
		/// Gets or sets the RootNodesViewModels value.
		/// </summary>
		public ObservableCollection<ElementModelViewModel> RootNodesViewModels
		{
			get { return GetValue<ObservableCollection<ElementModelViewModel>>(RootNodesViewModelsProperty); }
			set { SetValue(RootNodesViewModelsProperty, value); }
		}

		/// <summary>
		/// RootNodesViewModels property data.
		/// </summary>
		public static readonly PropertyData RootNodesViewModelsProperty = RegisterProperty("RootNodesViewModels", typeof(ObservableCollection<ElementModelViewModel>));

		#endregion


		#region SelectedItems property

		/// <summary>
		/// Gets or sets the SelectedItems value.
		/// </summary>
		public ObservableCollection<ElementModelViewModel> SelectedItems
		{
			get { return GetValue<ObservableCollection<ElementModelViewModel>>(SelectedItemsProperty); }
			set { SetValue(SelectedItemsProperty, value); }
		}

		/// <summary>
		/// SelectedItems property data.
		/// </summary>
		public static readonly PropertyData SelectedItemsProperty =
			RegisterProperty("SelectedItems", typeof(ObservableCollection<ElementModelViewModel>), null);


		private void SelectedItems_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
		{
			if (SelectedItems.Count == 1)
			{
				SelectedItem = SelectedItems.First();
			}
			else
			{
				SelectedItem = null;
			}
		}


		#endregion


		#region SelectedItem property

		/// <summary>
		/// Gets or sets the SelectedItem value.
		/// </summary>
		public ElementModelViewModel SelectedItem
		{
			get { return GetValue<ElementModelViewModel>(SelectedItemProperty); }
			set { SetValue(SelectedItemProperty, value); }
		}

		/// <summary>
		/// SelectedItem property data.
		/// </summary>
		public static readonly PropertyData SelectedItemProperty = RegisterProperty("SelectedItem", typeof(ElementModelViewModel));

		#endregion

		#region Commands

		#region CreateGroup command

		private Command _createGroupCommand;

		/// <summary>
		/// Gets the CreateGroup command.
		/// </summary>
		public Command CreateGroupCommand
		{
			get { return _createGroupCommand ?? (_createGroupCommand = new Command(CreateGroup, CanCreateGroup)); }
		}

		/// <summary>
		/// Method to invoke when the CreateGroup command is executed.
		/// </summary>
		private void CreateGroup()
		{
			var result = RequestNewGroupName(String.Empty);
			if (result.Result == MessageResult.OK)
			{
				PropModelServices.Instance().CreateGroupForElementModels(result.Response, SelectedItems.Select(x => x.ElementModel));
			}
		}

		/// <summary>
		/// Method to check whether the CreateGroup command can be executed.
		/// </summary>
		/// <returns><c>true</c> if the command can be executed; otherwise <c>false</c></returns>
		private bool CanCreateGroup()
		{
			return CanGroup();
		}

		#endregion

		#region MoveToGroup command

		private Command _moveToGroupCommand;

		/// <summary>
		/// Gets the MoveToGroup command.
		/// </summary>
		public Command MoveToGroupCommand
		{
			get { return _moveToGroupCommand ?? (_moveToGroupCommand = new Command(MoveToGroup, CanMoveToGroup)); }
		}

		/// <summary>
		/// Method to invoke when the MoveToGroup command is executed.
		/// </summary>
		private void MoveToGroup()
		{
			var result = RequestNewGroupName(String.Empty);
			if (result.Result == MessageResult.OK)
			{
				var parentToJoin = PropModelServices.Instance().CreateNode(result.Response);
				var pms = PropModelServices.Instance();
				foreach (var elementModelViewModel in SelectedItems)
				{
					ElementModel parentToLeave = (elementModelViewModel.ParentViewModel as ElementModelViewModel)?.ElementModel;
					if (parentToLeave != null)
					{
						pms.AddToParent(elementModelViewModel.ElementModel, parentToJoin);
						pms.RemoveFromParent(elementModelViewModel.ElementModel, parentToLeave);
					}
				}
				
			}
		}

		/// <summary>
		/// Method to check whether the MoveToGroup command can be executed.
		/// </summary>
		/// <returns><c>true</c> if the command can be executed; otherwise <c>false</c></returns>
		private bool CanMoveToGroup()
		{
			return CanGroup();
		}

		#endregion

		#region Rename command

		private Command _renameCommand;

		/// <summary>
		/// Gets the Rename command.
		/// </summary>
		public Command RenameCommand
		{
			get { return _renameCommand ?? (_renameCommand = new Command(Rename)); }
		}

		/// <summary>
		/// Method to invoke when the Rename command is executed.
		/// </summary>
		private void Rename()
		{
			if (SelectedItems.Count == 1)
			{
				MessageBoxService mbs = new MessageBoxService();
				var result = mbs.GetUserInput("Please enter the new name.", "Rename", SelectedItem.Name);
				if (result.Result == MessageResult.OK)
				{
					SelectedItems.First().Name = result.Response;
				}
			}
		}

		#endregion

		#region CreateNode command

		private Command _createNodeCommand;

		/// <summary>
		/// Gets the CreateNode command.
		/// </summary>
		public Command CreateNodeCommand
		{
			get { return _createNodeCommand ?? (_createNodeCommand = new Command(CreateNode, CanCreateNode)); }
		}

		/// <summary>
		/// Method to invoke when the CreateNode command is executed.
		/// </summary>
		private void CreateNode()
		{
			var result = RequestNewGroupName(PropModelServices.Instance().Uniquify(SelectedItem.Name));

			if (result.Result == MessageResult.OK)
			{
				PropModelServices.Instance().CreateNode(result.Response, SelectedItem.ElementModel);
				//Ensure parent is expanded
				SelectedItem.IsExpanded = true;
			}
			
		}

		/// <summary>
		/// Method to check whether the CreateNode command can be executed.
		/// </summary>
		/// <returns><c>true</c> if the command can be executed; otherwise <c>false</c></returns>
		private bool CanCreateNode()
		{
			return SelectedItems.Count == 1 && SelectedItem.ElementModel.IsGroupNode && SelectedItem.Children.All(c => c.IsGroupNode);
		}

		#endregion

		#endregion



		public void DeselectAll()
		{
			SelectedItems.ToList().ForEach(x => x.IsSelected = false);
			SelectedItems.Clear();
		}

		public void SelectModels(IEnumerable<ElementModelViewModel> elementModels)
		{
			foreach (var elementModelViewModel in elementModels)
			{
				elementModelViewModel.IsSelected = true;
				var parent = elementModelViewModel.ParentViewModel as ElementModelViewModel;
				if (parent != null)
				{
					parent.IsExpanded = true;
				}
			}
		}

		public void DeselectModels(IEnumerable<ElementModelViewModel> elementModels)
		{
			foreach (var elementModelViewModel in elementModels)
			{
				elementModelViewModel.IsSelected = false;
			}
		}

		private bool CanGroup()
		{
			var type = SelectedItems.FirstOrDefault()?.ElementType;
			return SelectedItems.Any() &&
			       SelectedItems.Select(x => x.ElementModel).All(x => x != Prop.RootNode && x.ElementType == type);
		}

		private static MessageBoxResponse RequestNewGroupName(string suggestedName)
		{
			MessageBoxService mbs = new MessageBoxService();
			return mbs.GetUserInput("Please enter the group name.", "Create Group", suggestedName);
		}

		public void Dispose()
		{
		}

		
		#region Implementation of IDropTarget

	    public static bool CanAcceptData(IDropInfo dropInfo)
	    {
	        if (dropInfo?.DragInfo == null)
	        {
	            return false;
	        }

	        if (!dropInfo.IsSameDragDropContextAsSource)
	        {
	            return false;
	        }

	        var isTreeViewItem = dropInfo.InsertPosition.HasFlag(RelativeInsertPosition.TargetItemCenter)
	                             && dropInfo.VisualTargetItem is TreeViewItem;
	        if (isTreeViewItem && dropInfo.VisualTargetItem == dropInfo.DragInfo.VisualSourceItem)
	        {
	            return false;
	        }

	        if (isTreeViewItem && dropInfo.InsertPosition.HasFlag(RelativeInsertPosition.TargetItemCenter))
	        {
	            var evmTarget = dropInfo.TargetItem as ElementModelViewModel;
	            if (evmTarget != null)
	            {
	                if (!evmTarget.ElementModel.IsGroupNode)
	                {
	                    return false;
	                }

	                IList<ElementModelViewModel> elementModelViewModels = dropInfo.Data as IList<ElementModelViewModel>;
	                if (elementModelViewModels != null)
	                {
	                    var isGroups = elementModelViewModels.Any(x => x.ElementModel.IsGroupNode);
	                    var isLeafs = elementModelViewModels.Any(x => x.IsLeaf);

                        if ( isGroups && isLeafs )
	                    {
	                        return false;
                        }

	                    if (isGroups && !evmTarget.ElementModel.CanAddGroupNodes)
	                    {
	                        return false;
	                    }

	                    if (isLeafs && evmTarget.ElementModel.CanAddGroupNodes)
	                    {
	                        return false;
	                    }
	                }
	            }
	        }

	        if (dropInfo.DragInfo.SourceCollection == dropInfo.TargetCollection)
	        {
	            var targetList = dropInfo.TargetCollection.TryGetList();
	            return targetList != null;
	        }

	        if (dropInfo.TargetCollection == null)
	        {
	            return false;
	        }
	       
	        if (TestCompatibleTypes(dropInfo.TargetCollection, dropInfo.Data))
	        {
	            var isChildOf = IsChildOf(dropInfo.VisualTargetItem, dropInfo.DragInfo.VisualSourceItem);
	            return !isChildOf;
	        }
	        
	        return false;
	    }


	    /// <summary>
	    /// Determines whether the data of the drag drop action should be copied otherwise moved.
	    /// </summary>
	    /// <param name="dropInfo">The DropInfo with a valid DragInfo.</param>
	    public static bool ShouldCopyData(IDropInfo dropInfo)
	    {
	        // default should always the move action/effect
	        if (dropInfo?.DragInfo == null)
	        {
	            return false;
	        }
	        var copyData = ((dropInfo.DragInfo.DragDropCopyKeyState != default(DragDropKeyStates)) && dropInfo.KeyStates.HasFlag(dropInfo.DragInfo.DragDropCopyKeyState))
	                       || dropInfo.DragInfo.DragDropCopyKeyState.HasFlag(DragDropKeyStates.LeftMouseButton);
	        copyData = copyData
	                   && !(dropInfo.DragInfo.SourceItem is HeaderedContentControl)
	                   && !(dropInfo.DragInfo.SourceItem is HeaderedItemsControl)
	                   && !(dropInfo.DragInfo.SourceItem is ListBoxItem);
	        return copyData;
	    }

        /// <inheritdoc />
        public void DragOver(IDropInfo dropInfo)
		{
		    if (CanAcceptData(dropInfo))
		    {
		        dropInfo.Effects = ShouldCopyData(dropInfo) ? DragDropEffects.Copy : DragDropEffects.Move;
		        var isTreeViewItem = dropInfo.InsertPosition.HasFlag(RelativeInsertPosition.TargetItemCenter) && dropInfo.VisualTargetItem is TreeViewItem;
		        dropInfo.DropTargetAdorner = isTreeViewItem ? DropTargetAdorners.Highlight : DropTargetAdorners.Insert;
		    }
        }

		/// <inheritdoc />
		public void Drop(IDropInfo dropInfo)
		{
			var models = dropInfo.Data as IList<ElementModelViewModel>;
			if (models != null)
			{
				var pms = PropModelServices.Instance();
				var targetModel = dropInfo.TargetItem as ElementModelViewModel;
				var targetModelParent = targetModel?.ParentViewModel as ElementModelViewModel;

				SelectedItems.Clear();

				if (targetModel != null && targetModelParent != null)
				{
					foreach (var elementModelViewModel in models.Reverse())
					{
						if (dropInfo.Effects == DragDropEffects.Move)
						{
						   //Get our parent 
						    var parentVm = elementModelViewModel.ParentViewModel as ElementModelViewModel;

                            if (dropInfo.InsertPosition == RelativeInsertPosition.BeforeTargetItem)
						    {
						        //We are inserting into a range.
                                //Ensure the parent is a group node.
                                if (parentVm != null &&  parentVm.ElementModel.IsGroupNode)
						        {
						            pms.RemoveFromParent(elementModelViewModel.ElementModel, parentVm.ElementModel);
						            pms.InsertToParent(elementModelViewModel.ElementModel, targetModelParent.ElementModel, dropInfo.InsertIndex);
                                }

                            }
						    else if(dropInfo.InsertPosition == RelativeInsertPosition.AfterTargetItem)
						    {
                                Console.Out.WriteLine("Insert After");
                            }
						    else
						    {
                                //We are on the center and adding to a group hopefully
						        //Ensure the target is a group node.
						        if (targetModel.ElementModel.IsGroupNode && parentVm != null)
						        {
						            pms.AddToParent(elementModelViewModel.ElementModel, targetModel.ElementModel);
                                    pms.RemoveFromParent(elementModelViewModel.ElementModel, parentVm.ElementModel);
                                }
						        else
						        {
                                    Console.Out.WriteLine("Adding to center of non group node");
						        }
                            }
						}
					}
				}
			}

		}

	    private static bool IsChildOf(UIElement targetItem, UIElement sourceItem)
	    {
	        var parent = ItemsControl.ItemsControlFromItemContainer(targetItem);

	        while (parent != null)
	        {
	            if (parent == sourceItem)
	            {
	                return true;
	            }

	            parent = ItemsControl.ItemsControlFromItemContainer(parent);
	        }

	        return false;
	    }

	    private static bool TestCompatibleTypes(IEnumerable target, object data)
	    {
	        TypeFilter filter = (t, o) => { return (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>)); };

	        var enumerableInterfaces = target.GetType().FindInterfaces(filter, null);
	        var enumerableTypes = from i in enumerableInterfaces
	            select i.GetGenericArguments().Single();

	        if (enumerableTypes.Any())
	        {
	            var dataType = TypeUtilities.GetCommonBaseClass(ExtractData(data));
	            return enumerableTypes.Any(t => t.IsAssignableFrom(dataType));
	        }
	        else
	        {
	            return target is IList;
	        }
	    }

	    public static IEnumerable ExtractData(object data)
	    {
	        if (data is IEnumerable && !(data is string))
	        {
	            return (IEnumerable)data;
	        }
	        else
	        {
	            return Enumerable.Repeat(data, 1);
	        }
	    }

        #endregion

        #region Implementation of IDragSource

        /// <inheritdoc />
        public void StartDrag(IDragInfo dragInfo)
		{

            //In our case, the Treeview does not support multiple items, so the drag behavior can't figure it out
            //So we will take care of it ourselves.
		    var itemCount = dragInfo.SourceItems.Cast<object>().Count();

		    if (itemCount == 1 && SelectedItems.Count == 1)
		    {
		        dragInfo.Data = TypeUtilities.CreateDynamicallyTypedList(new[] { dragInfo.SourceItems.Cast<object>().First() });
			}
		    else if (itemCount > 1)
		    {
		        dragInfo.Data = TypeUtilities.CreateDynamicallyTypedList(dragInfo.SourceItems);
            }
            else if (SelectedItems.Count > 1 && itemCount == 1)
		    {
		        if (SelectedItems.Contains(dragInfo.SourceItems.Cast<object>().First()))
		        {
		            dragInfo.Data = TypeUtilities.CreateDynamicallyTypedList(SelectedItems);
				}
		        else
		        {
                    dragInfo.Data = TypeUtilities.CreateDynamicallyTypedList(new[] { dragInfo.SourceItems.Cast<object>().First() });
				}
		    }

		    dragInfo.Effects = (dragInfo.Data != null) ? DragDropEffects.Copy | DragDropEffects.Move : DragDropEffects.None;
        }

		/// <inheritdoc />
		public bool CanStartDrag(IDragInfo dragInfo)
		{
		    return true;
		}

		/// <inheritdoc />
		public void Dropped(IDropInfo dropInfo)
		{
			
		}

		/// <inheritdoc />
		public void DragCancelled()
		{
			
		}

		/// <inheritdoc />
		public bool TryCatchOccurredException(Exception exception)
		{
		    return false;
        }

		#endregion
	}
}
