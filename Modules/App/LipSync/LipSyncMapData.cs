﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.Serialization;
using Vixen.Module;
using Vixen.Sys;
using Common.Controls.ColorManagement.ColorModels;

namespace VixenModules.App.LipSyncApp
{
	public class LipSyncMapData : ModuleDataModelBase
	{
		public LipSyncMapData()
		{
			MapItems = new List<LipSyncMapItem>();
			MapItems.Add(new LipSyncMapItem());
			IsDefaultMapping = false;
			StringsAreRows = false;
			GroupsAllowed = false;
			RecursionAllowed = true;
			IsMatrix = false;
			Notes = string.Empty;
			UsingDefaults = true;

			StartNode = "";
		}

		public LipSyncMapData(List<string> stringNames)
		{
			int stringNum = 0;
			MapItems = new List<LipSyncMapItem>();
			foreach(string stringName in stringNames)
			{
				MapItems.Add(new LipSyncMapItem(stringName,stringNum++));
			}
			StartNode = "";
			StringsAreRows = false;
			GroupsAllowed = false;
			RecursionAllowed = true;
			IsMatrix = false;
			UsingDefaults = false;

			Notes = "";
		}

		public LipSyncMapData(LipSyncMapData mapSetup)
		{
			MapItems = new List<LipSyncMapItem>(mapSetup.MapItems);
			IsCurrentLibraryMapping = mapSetup.IsCurrentLibraryMapping;
			LibraryReferenceName = (string)mapSetup.LibraryReferenceName.Clone();
			IsDefaultMapping = mapSetup.IsDefaultMapping;
			StringCount = mapSetup.StringCount;
			StartNode = mapSetup.StartNode;
			StringsAreRows = mapSetup.StringsAreRows;
			GroupsAllowed = mapSetup.GroupsAllowed;
			RecursionAllowed = mapSetup.RecursionAllowed;
			IsMatrix = mapSetup.IsMatrix;
			Notes = mapSetup.Notes;
			UsingDefaults = mapSetup.UsingDefaults;
		}

		public override IModuleDataModel Clone()
		{
			LipSyncMapData newInstance = new LipSyncMapData();
			newInstance.MapItems = new List<LipSyncMapItem>();

			foreach (LipSyncMapItem item in MapItems)
			{
				newInstance.MapItems.Add(item.Clone());
			}
			newInstance.StringCount = StringCount;
			newInstance.LibraryReferenceName = LibraryReferenceName;
			newInstance.IsDefaultMapping = false;
			newInstance.StartNode = StartNode;
			newInstance.StringsAreRows = StringsAreRows;
			newInstance.GroupsAllowed = GroupsAllowed;
			newInstance.RecursionAllowed = RecursionAllowed;
			newInstance.IsMatrix = IsMatrix;
			newInstance.Notes = Notes;
			newInstance.UsingDefaults = UsingDefaults;

			return newInstance;
		}

		[DataMember]
		public int StringCount { get; set; }

		//Deprecated
		[DataMember(EmitDefaultValue = false)]
		[Obsolete("No longer used.", false)]
		public int MatrixStringCount { get; set; }

		//Deprecated
		[DataMember(EmitDefaultValue = false)]
		[Obsolete("No longer used.", false)]
		public int MatrixPixelsPerString { get; set; }

		[DataMember]
		public bool IsMatrix { get; set; }

		[DataMember]
		public string StartNode { get; set; }

		//Deprecated
		[DataMember(EmitDefaultValue = false)]
		[Obsolete("No longer used.", false)]
		public int ZoomLevel { get; set; }

		[DataMember]
		public bool StringsAreRows { get; set; }

		[DataMember]
		public List<LipSyncMapItem> MapItems { get; set; }

		[DataMember]
		public bool IsCurrentLibraryMapping { get; set; }

		[DataMember]
		public bool IsDefaultMapping { get; set; }

		[DataMember]
		public bool GroupsAllowed { get; set; }

		[DataMember]
		public bool RecursionAllowed { get; set; }

		[DataMember]
		protected string _libraryReferenceName;

		[DataMember]
		public string PictureDirectory
		{
			get
			{
				return Paths.ModuleDataFilesPath + "\\LipSync\\" + LibraryReferenceName + "\\";
			}
		}

		[DataMember]
		public string Notes { get; set; }

		[DataMember]
		public bool UsingDefaults { get; set; }

		public string PictureFileName(PhonemeType phoneme)
		{
			return Path.Combine(PictureDirectory, $"{phoneme}.bmp");
		}

		public string PictureFileName(string phoneme)
		{
			return Path.Combine(PictureDirectory, $"{phoneme}.bmp");
		}

		public string LibraryReferenceName
		{
			get
			{
				if (_libraryReferenceName == null)
					return string.Empty;
				else
					return _libraryReferenceName;
			}
			set { _libraryReferenceName = value; }
		}

		public LipSyncMapItem FindMapItem(Guid id)
		{
			return MapItems.Find(x => x.ElementGuid.Equals(id));
		}

		public Tuple<double, Color> ConfiguredColorAndIntensity(Guid id)
		{
			var item = FindMapItem(id);
			return ConfiguredColorAndIntensity(item);
		}

		public Tuple<double, Color> ConfiguredColorAndIntensity(LipSyncMapItem item)
		{
			double intensityRetVal = 0;
			Color colorRetVal = Color.Black;

			if (!IsMatrix)
			{
				if (item != null)
				{
					HSV hsvVal = HSV.FromRGB(item.ElementColor);
					hsvVal.V = 1;
					colorRetVal = hsvVal.ToRGB().ToArgb();
					intensityRetVal = HSV.VFromRgb(item.ElementColor);
				}
			}
			
			return new Tuple<double, Color>(intensityRetVal, colorRetVal);
		}

		public double ConfiguredIntensity(Guid id)
		{
			var item = FindMapItem(id);
			return ConfiguredIntensity(item);
		}

		public double ConfiguredIntensity(LipSyncMapItem item)
		{
			double retVal = 0;

			if (!IsMatrix)
			{
				if (item != null)
				{
					retVal = HSV.VFromRgb(item.ElementColor);
				}
			}

			return retVal;
		}

		public Color ConfiguredColor(Guid id)
		{
			var item = FindMapItem(id);
			return ConfiguredColor(item);
		}

		public Color ConfiguredColor(LipSyncMapItem item)
		{
			Color retVal = Color.Black;

			if (!IsMatrix)
			{
				if (item != null)
				{
					HSV hsvVal = HSV.FromRGB(item.ElementColor);
					hsvVal.V = 1;
					retVal = hsvVal.ToRGB().ToArgb();
				}
			}
			
			return retVal;
		}

		public bool PhonemeState(Guid id, string phonemeName)
		{
			var item = FindMapItem(id);
			return PhonemeState(phonemeName, item);
		}

		public bool PhonemeState(string phonemeName, LipSyncMapItem item)
		{
			bool retVal = false;

			item?.PhonemeList.TryGetValue(phonemeName, out retVal);

			return retVal;
		}

		public bool IsFaceComponentType(FaceComponent type, LipSyncMapItem item)
		{
			bool retVal = false;

			item?.FaceComponents.TryGetValue(type, out retVal);

			return retVal;
		}

		//public bool IsNonMouth(Guid id)
		//{
		//	var item = FindMapItem(id);
		//	return item.FaceComponents.Any(x => x.Key != FaceComponent.Mouth);
		//}

		//public List<FaceComponent> GetFaceComponents(Guid id)
		//{
		//	var item = FindMapItem(id);
		//	return item.FaceComponents.Where(x => x.Value).Select(f => f.Key).ToList();
		//}

		public override string ToString()
		{
			return LibraryReferenceName;
		}
	   
	}
}
