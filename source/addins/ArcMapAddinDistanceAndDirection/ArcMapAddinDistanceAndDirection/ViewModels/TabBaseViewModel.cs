﻿// Copyright 2016 Esri 
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//    http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

// System
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Collections.ObjectModel;

// Esri
using ESRI.ArcGIS.esriSystem;
using ESRI.ArcGIS.ArcMapUI;
using ESRI.ArcGIS.Carto;
using ESRI.ArcGIS.Geometry;
using ESRI.ArcGIS.Display;
using ESRI.ArcGIS.Geodatabase;

using DistanceAndDirectionLibrary;
using DistanceAndDirectionLibrary.Helpers;
using DistanceAndDirectionLibrary.ViewModels;
using ArcMapAddinDistanceAndDirection.Models;
using DistanceAndDirectionLibrary.Models;
using DistanceAndDirectionLibrary.Views;

namespace ArcMapAddinDistanceAndDirection.ViewModels
{
    /// <summary>
    /// Base class for all the common properties, commands and events for tab items
    /// </summary>
    public class TabBaseViewModel : BaseViewModel
    {
        public const System.String MAP_TOOL_NAME = "Esri_ArcMapAddinDistanceAndDirection_MapPointTool";

        public TabBaseViewModel()
        {
            //properties
            LineType = LineTypes.Geodesic;
            LineDistanceType = DistanceTypes.Meters;

            //commands
            SaveAsCommand = new RelayCommand(OnSaveAs);
            ClearGraphicsCommand = new RelayCommand(OnClearGraphics);
            ActivateToolCommand = new RelayCommand(OnActivateTool);
            EnterKeyCommand = new RelayCommand(OnEnterKeyCommand);
            EditPropertiesDialogCommand = new RelayCommand(OnEditPropertiesDialogCommand);

            // Mediator
            Mediator.Register(Constants.NEW_MAP_POINT, OnNewMapPointEvent);
            Mediator.Register(Constants.MOUSE_MOVE_POINT, OnMouseMoveEvent);
            Mediator.Register(Constants.TAB_ITEM_SELECTED, OnTabItemSelected);
            Mediator.Register(Constants.KEYPRESS_ESCAPE, OnKeypressEscape);
            Mediator.Register(Constants.POINT_TEXT_KEYDOWN, OnPointTextBoxKeyDown);
            Mediator.Register(Constants.TOC_ITEMS_CHANGED, onActiveViewChagedEvent);

            configObserver = new PropertyObserver<DistanceAndDirectionConfig>(DistanceAndDirectionConfig.AddInConfig)
            .RegisterHandler(n => n.DisplayCoordinateType, n =>
            {
                RaisePropertyChanged(() => Point1Formatted);
                RaisePropertyChanged(() => Point2Formatted);
            });

        }

        PropertyObserver<DistanceAndDirectionConfig> configObserver;

        #region Properties

        // lists to store GUIDs of graphics, temp feedback and map graphics
        private static ObservableCollection<Graphic> GraphicsList = new ObservableCollection<Graphic>();

        internal bool HasPoint1 = false;
        internal bool HasPoint2 = false;
        internal bool HasPoint3 = false;
        internal INewLineFeedback feedback = null;
        internal FeatureClassUtils fcUtils = new FeatureClassUtils();
        internal KMLUtils kmlUtils = new KMLUtils();
        internal SaveFileDialog sfDlg = null;

        //public static DistanceAndDirectionConfig AddInConfig = new DistanceAndDirectionConfig(); 

        public bool HasMapGraphics
        {
            get
            {
                if (this is LinesViewModel)
                {
                    return GraphicsList.Any(g => g.GraphicType == GraphicTypes.Line);
                }
                else if (this is CircleViewModel)
                {
                    return GraphicsList.Any(g => g.GraphicType == GraphicTypes.Circle);
                }
                else if (this is EllipseViewModel)
                {
                    return GraphicsList.Any(g => g.GraphicType == GraphicTypes.Ellipse);
                }
                else if (this is RangeViewModel)
                {
                    return GraphicsList.Any(g => g.GraphicType == GraphicTypes.RangeRing);
                }

                return false;
            }
        }

        private IPoint point1 = null;
        /// <summary>
        /// Property for the first IPoint
        /// </summary>
        public virtual IPoint Point1
        {
            get
            {
                return point1;
            }
            set
            {
                // do not add anything to the map from here
                point1 = value;
                RaisePropertyChanged(() => Point1);
                RaisePropertyChanged(() => Point1Formatted);
            }
        }

        private IPoint point2 = null;
        /// <summary>
        /// Property for the second IPoint
        /// Not all tools need a second point
        /// </summary>
        public virtual IPoint Point2
        {
            get
            {
                return point2;
            }
            set
            {
                point2 = value;
                RaisePropertyChanged(() => Point2);
                RaisePropertyChanged(() => Point2Formatted);
            }
        }

        string point1Formatted = string.Empty;
        /// <summary>
        /// String property for the first IPoint
        /// This is used to format the point for the UI and allow string input of different types of coordinates
        /// </summary>
        public string Point1Formatted
        {
            get
            {
                // return a formatted first point depending on how it was entered, manually or via map point tool
                if (string.IsNullOrWhiteSpace(point1Formatted))
                {
                    if (Point1 != null)
                    {
                        // only format if the Point1 data was generated from a mouse click
                        string outFormattedString = string.Empty;
                        CoordinateConversionLibrary.Models.CoordinateType ccType = CoordinateConversionLibrary.Helpers.ConversionUtils.GetCoordinateString(GetFormattedPoint(Point1), out outFormattedString);
                        return outFormattedString;
                    }
                    return string.Empty;
                }
                else
                {
                    // this was user inputed so just return the inputed string
                    return point1Formatted;
                }
            }

            set
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    if (!IsToolActive)
                        point1 = null; // reset the point if the user erased (TRICKY: tool sets to "" on click)

                    point1Formatted = string.Empty;
                    RaisePropertyChanged(() => Point1Formatted);
                    return;
                }
                // try to convert string to an IPoint
                string outFormattedString = string.Empty;
                CoordinateConversionLibrary.Models.CoordinateType ccType = CoordinateConversionLibrary.Helpers.ConversionUtils.GetCoordinateString(value, out outFormattedString);
                IPoint point = (ccType != CoordinateConversionLibrary.Models.CoordinateType.Unknown) ? GetPointFromString(outFormattedString) : null;
                if (point != null)
                {
                    // clear temp graphics
                    ClearTempGraphics();
                    point1Formatted = value;
                    HasPoint1 = true;
                    Point1 = point;
                    var color = new RgbColorClass() { Green = 255 } as IColor;
                    IDictionary<String, System.Object> ptAttributes = new Dictionary<String, System.Object>();
                    ptAttributes.Add("X", Point1.X);
                    ptAttributes.Add("Y", Point1.Y);
                    AddGraphicToMap(Point1, color, true, esriSimpleMarkerStyle.esriSMSCircle, esriRasterOpCode.esriROPNOP, ptAttributes);

                    // lets try feedback
                    // Avoid null reference exception during automated testing
                    if ((ArcMap.Application != null) && (ArcMap.Document != null))
                    {
                        var av = ArcMap.Document.FocusMap as IActiveView;
                        if (av == null)
                            return;

                        point.Project(ArcMap.Document.FocusMap.SpatialReference);
                        CreateFeedback(point, av);
                        feedback.Start(point);
                        if (Point2 != null)
                        {
                            UpdateDistance(GetGeoPolylineFromPoints(Point1, Point2));
                            FeedbackMoveTo(Point2);
                        }
                    }
                }
                else
                {
                    // invalid coordinate, reset and throw exception
                    Point1 = null;
                    HasPoint1 = false;
                    throw new ArgumentException(DistanceAndDirectionLibrary.Properties.Resources.AEInvalidCoordinate);
                }
            }
        }

        string point2Formatted = string.Empty;
        /// <summary>
        /// String property for the second IPoint
        /// This is used to format the point for the UI and allow string input of different types of coordinates
        /// Input types like GARS, MGRS, USNG, UTM
        /// </summary>
        public string Point2Formatted
        {
            get
            {
                // return a formatted second point depending on how it was entered, manually or via map point tool
                if (string.IsNullOrWhiteSpace(point2Formatted))
                {
                    if (Point1 != null)
                    {
                        // only format if the Point1 data was generated from a mouse click
                        string outFormattedString = string.Empty;
                        CoordinateConversionLibrary.Models.CoordinateType ccType = CoordinateConversionLibrary.Helpers.ConversionUtils.GetCoordinateString(GetFormattedPoint(Point2), out outFormattedString);
                        return outFormattedString;
                    }
                    return string.Empty;
                }
                else
                {
                    // this was user inputed so just return the inputed string
                    return point2Formatted;
                }
            }
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    if (!IsToolActive)
                        point2 = null; // reset the point if the user erased (TRICKY: tool sets to "" on click)

                    point2Formatted = string.Empty;
                    RaisePropertyChanged(() => Point2Formatted);
                    return;
                }

                // Point1Formatted should never equal to Point2Formatted
                if (Point1Formatted.ToLower().Trim().Equals(value.ToLower().Trim()))
                {
                    Point2 = null;
                    HasPoint2 = false;
                    throw new ArgumentException(DistanceAndDirectionLibrary.Properties.Resources.EndPointAndStartPointSameError);
                }

                // try to convert string to an IPoint
                string outFormattedString = string.Empty;
                CoordinateConversionLibrary.Models.CoordinateType ccType = CoordinateConversionLibrary.Helpers.ConversionUtils.GetCoordinateString(value, out outFormattedString);
                IPoint point = (ccType != CoordinateConversionLibrary.Models.CoordinateType.Unknown) ? GetPointFromString(outFormattedString) : null;
                if (point != null)
                {
                    point2Formatted = value;
                    //HasPoint2 = true;
                    Point2 = point;

                    if ((ArcMap.Document == null) || (ArcMap.Document.FocusMap == null))
                        return;

                    var av = ArcMap.Document.FocusMap as IActiveView;
                    if (av == null)
                        return;

                    Point2.Project(ArcMap.Document.FocusMap.SpatialReference);

                    if (HasPoint1)
                    {
                        // lets try feedback
                        CreateFeedback(Point1, av);
                        feedback.Start(Point1);
                        UpdateDistance(GetGeoPolylineFromPoints(Point1, Point2));
                        // I have to create a new point here, otherwise "MoveTo" will change the spatial reference to world mercator
                        FeedbackMoveTo(point);
                    }

                }
                else
                {
                    // invalid coordinate, reset and throw exception
                    Point2 = null;
                    HasPoint2 = false;
                    throw new ArgumentException(DistanceAndDirectionLibrary.Properties.Resources.AEInvalidCoordinate);
                }
            }
        }


        private bool isActiveTab = false;
        /// <summary>
        /// Property to keep track of which tab/viewmodel is the active item
        /// </summary>
        public bool IsActiveTab
        {
            get
            {
                return isActiveTab;
            }
            set
            {
                Reset(true);
                isActiveTab = value;
                RaisePropertyChanged(() => IsActiveTab);
            }
        }

        DistanceTypes lineDistanceType = DistanceTypes.Meters;
        /// <summary>
        /// Property for the distance type
        /// </summary>
        public virtual DistanceTypes LineDistanceType
        {
            get { return lineDistanceType; }
            set
            {
                //var before = lineDistanceType;
                lineDistanceType = value;
                //Distance = ConvertFromTo(before, value, Distance);
                UpdateFeedback();
            }
        }

        internal virtual void UpdateFeedback()
        {

        }

        double distance = 0.0;
        /// <summary>
        /// Property for the distance/length
        /// </summary>
        public virtual double Distance
        {
            get { return distance; }
            set
            {
                if (value < 0.0)
                    throw new ArgumentException(DistanceAndDirectionLibrary.Properties.Resources.AEMustBePositive);

                distance = value;
                DistanceString = distance.ToString("0.##");
                RaisePropertyChanged(() => Distance);
                RaisePropertyChanged(() => DistanceString);
            }
        }

        protected string distanceString = String.Empty;
        /// <summary>
        /// Distance property as a string
        /// </summary>
        public virtual string DistanceString
        {
            get
            {
                return Distance.ToString("0.##");              
            }
            set
            {
                // lets avoid an infinite loop here
                if (string.Equals(distanceString, value))
                    return;

                distanceString = value;

                // update distance
                double d = 0.0;
                if (double.TryParse(distanceString, out d))
                {
                    Distance = d;
                }
                else
                {
                    throw new ArgumentException(DistanceAndDirectionLibrary.Properties.Resources.AEInvalidInput);
                }
            }
        }

        /// <summary>
        /// Property for the type of geodesy line
        /// </summary>
        public LineTypes LineType { get; set; }

        /// <summary>
        /// Property used to test if there is enough info to create a line map element
        /// </summary>
        public virtual bool CanCreateElement
        {
            get
            {
                return (Point1 != null && Point2 != null);
            }
        }

        /// <summary>
        /// Property to determine if map tool is enabled or disabled
        /// </summary>
        public virtual bool IsToolActive
        {
            get
            {
                if ((ArcMap.Application != null) && (ArcMap.Application.CurrentTool != null))
                    return ArcMap.Application.CurrentTool.Name == MAP_TOOL_NAME;

                return false;
            }

            set
            {
                if (value)
                    OnActivateTool(null);
                else
                    if (ArcMap.Application.CurrentTool != null)
                    ArcMap.Application.CurrentTool = null;

                RaisePropertyChanged(() => IsToolActive);
            }
        }
        #endregion Properties

        #region Commands

        public DistanceAndDirectionLibrary.Helpers.RelayCommand SaveAsCommand { get; set; }
        public DistanceAndDirectionLibrary.Helpers.RelayCommand ClearGraphicsCommand { get; set; }
        public DistanceAndDirectionLibrary.Helpers.RelayCommand ActivateToolCommand { get; set; }
        public DistanceAndDirectionLibrary.Helpers.RelayCommand EnterKeyCommand { get; set; }
        public DistanceAndDirectionLibrary.Helpers.RelayCommand EditPropertiesDialogCommand { get; set; }


        #endregion

        /// <summary>
        /// Method is called when a user pressed the "Enter" key or when a second point is created for a line from mouse clicks
        /// Derived class must override this method in order to create map elements
        /// Clears temp graphics by default
        /// </summary>
        internal virtual IGeometry CreateMapElement()
        {
            ClearTempGraphics();
            return null;
        }

        #region Private Event Functions

        /// <summary>
        /// Clears all the graphics from the maps graphic container
        /// Inlucdes temp and map graphics
        /// Only removes temp and map graphics that were created by this add-in
        /// </summary>
        /// <param name="obj"></param>
        private void OnClearGraphics(object obj)
        {
            var mxdoc = ArcMap.Application.Document as IMxDocument;
            if (mxdoc == null)
                return;
            var av = mxdoc.FocusMap as IActiveView;
            if (av == null)
                return;
            var gc = av as IGraphicsContainer;
            if (gc == null)
                return;

            RemoveGraphics(gc, false);

            //gc.DeleteAllElements();
            //av.Refresh();
            av.PartialRefresh(esriViewDrawPhase.esriViewGraphics, null, null);

            RaisePropertyChanged(() => HasMapGraphics);
        }

        /// <summary>
        /// Saves graphics to file gdb or shp file
        /// </summary>
        /// <param name="obj"></param>
        private void OnSaveAs(object obj)
        {
            var dlg = new GRSaveAsFormatView();
            dlg.DataContext = new SaveAsFormatViewModel();
            var vm =  (SaveAsFormatViewModel)dlg.DataContext;

            if (dlg.ShowDialog() == true)
            {
                IFeatureClass fc = null;

                // Get the graphics list for the selected tab
                List<Graphic> typeGraphicsList = new List<Graphic>();
                if (this is LinesViewModel)
                {
                    typeGraphicsList = GraphicsList.Where(g => g.GraphicType == GraphicTypes.Line).ToList();
                }
                else if (this is CircleViewModel)
                {
                    typeGraphicsList = GraphicsList.Where(g => g.GraphicType == GraphicTypes.Circle).ToList();
                }
                else if (this is EllipseViewModel)
                {
                    typeGraphicsList = GraphicsList.Where(g => g.GraphicType == GraphicTypes.Ellipse).ToList();
                }
                else if (this is RangeViewModel)
                {
                    typeGraphicsList = GraphicsList.Where(g => g.GraphicType == GraphicTypes.RangeRing).ToList();
                }

                string path = null;
                if (vm.FeatureShapeIsChecked)
                {
                    path = fcUtils.PromptUserWithGxDialog(ArcMap.Application.hWnd);
                    if (path != null)
                    {
                        if (System.IO.Path.GetExtension(path).Equals(".shp"))
                        {
                            fc = fcUtils.CreateFCOutput(path, SaveAsType.Shapefile, typeGraphicsList, ArcMap.Document.FocusMap.SpatialReference);
                        }
                        else
                        {
                            fc = fcUtils.CreateFCOutput(path, SaveAsType.FileGDB, typeGraphicsList, ArcMap.Document.FocusMap.SpatialReference);
                        }
                    }
                }
                else
                {
                    path = PromptSaveFileDialog();
                    if (path != null)
                    {
                        string kmlName = System.IO.Path.GetFileName(path);
                        string folderName = System.IO.Path.GetDirectoryName(path);
                        string tempShapeFile = folderName + 
                            System.IO.Path.DirectorySeparatorChar
                            + "tmpShapefile" + Guid.NewGuid().ToString() + ".shp";
                        IFeatureClass tempFc = fcUtils.CreateFCOutput(tempShapeFile, SaveAsType.Shapefile, typeGraphicsList, ArcMap.Document.FocusMap.SpatialReference);

                        if (tempFc != null)
                        {
                            kmlUtils.ConvertLayerToKML(path, tempShapeFile, ArcMap.Document.FocusMap);

                            MessageBox.Show(
                                    DistanceAndDirectionLibrary.Properties.Resources.KMZExportComplete,
                                    DistanceAndDirectionLibrary.Properties.Resources.KMZExportComplete);
                                    
                            // delete the temporary shapefile
                            fcUtils.DeleteShapeFile(tempShapeFile);
                        }
                    }
                }

                if (fc != null)
                {
                    AddFeatureLayerToMap(fc);
                }
            }
        }

        /// <summary>
        /// Add the feature layer to the map 
        /// </summary>
        /// <param name="fc">IFeatureClass</param>
        private void AddFeatureLayerToMap(IFeatureClass fc)
        {
            if ((ArcMap.Document == null) || (ArcMap.Document.FocusMap == null))
                return;

            IFeatureLayer outputFeatureLayer = new FeatureLayerClass();
            outputFeatureLayer.FeatureClass = fc;

            IGeoFeatureLayer geoLayer = (IGeoFeatureLayer)outputFeatureLayer;

            if (geoLayer.FeatureClass.ShapeType != esriGeometryType.esriGeometryPolyline)
            {
                IFeatureRenderer pFeatureRender;
                pFeatureRender = (IFeatureRenderer)new SimpleRenderer();
                ISimpleFillSymbol pSimpleFillSymbol = new SimpleFillSymbolClass();
                pSimpleFillSymbol.Style = esriSimpleFillStyle.esriSFSHollow;
                pSimpleFillSymbol.Outline.Width = 0.4;

                ISimpleRenderer pSimpleRenderer;
                pSimpleRenderer = new SimpleRenderer();
                pSimpleRenderer.Symbol = (ISymbol)pSimpleFillSymbol;

                geoLayer.Renderer = (IFeatureRenderer)pSimpleRenderer;
            }

            geoLayer.Name = fc.AliasName;

            ESRI.ArcGIS.Carto.IMap map = ArcMap.Document.FocusMap;

            map.AddLayer((ILayer)outputFeatureLayer);
        }

        private string PromptSaveFileDialog()
        {
            if (sfDlg == null)
            {
                sfDlg = new SaveFileDialog();
                sfDlg.AddExtension = true;
                sfDlg.CheckPathExists = true;
                sfDlg.DefaultExt = "kmz";
                sfDlg.Filter = "KMZ File (*.kmz)|*.kmz";
                sfDlg.OverwritePrompt = true;
                sfDlg.Title = "Choose location to create KMZ file";

            }
            sfDlg.FileName = "";

            if (sfDlg.ShowDialog() == DialogResult.OK)
            {
                return sfDlg.FileName;
            }

            return null;
        }

        /// <summary>
        /// Method to clear all temp graphics
        /// </summary>
        internal void ClearTempGraphics()
        {
            // Indicates we are running an automated test and as such we do not want to
            // proceed and generate a NullReferenceException
            if (ArcMap.Application == null)
                return;

            var mxdoc = ArcMap.Application.Document as IMxDocument;
            if (mxdoc == null)
                return;
            var av = mxdoc.FocusMap as IActiveView;
            if (av == null)
                return;
            var gc = av as IGraphicsContainer;
            if (gc == null)
                return;

            RemoveGraphics(gc, true);

            av.PartialRefresh(esriViewDrawPhase.esriViewAll, null, null);
            RaisePropertyChanged(() => HasMapGraphics);
        }

        /// <summary>
        /// Method used to remove graphics from the graphics container
        /// Elements are tagged with a GUID on the IElementProperties.Name property
        /// Removes graphics from all tabs, not just the tab that is currently active
        /// </summary>
        private void RemoveGraphics(IGraphicsContainer gc, bool removeOnlyTemporary)
        {
            if (gc == null || !GraphicsList.Any())
                return;

            // keep track of the graphics that we need to remove from the GraphicsList
            List<Graphic> removedGraphics = new List<Graphic>();

            var elementList = new List<IElement>();
            gc.Reset();
            var element = gc.Next();
            while (element != null)
            {
                var eleProps = (IElementProperties)element;
                foreach (Graphic graphic in GraphicsList)
                {
                    if (graphic.UniqueId.Equals(eleProps.Name) && graphic.ViewModel == this)
                    {
                        if (graphic.IsTemp == removeOnlyTemporary)
                        {
                            elementList.Add(element);
                            removedGraphics.Add(graphic);
                        }

                        break;
                    }
                }

                element = gc.Next();
            }

            foreach (var ele in elementList)
            {
                gc.DeleteElement(ele);
            }

            // clean up the GraphicsList and remove the necessary graphics from it
            foreach (Graphic graphic in removedGraphics)
            {
                GraphicsList.Remove(graphic);
            }

            elementList.Clear();
            RaisePropertyChanged(() => HasMapGraphics);
        }

        /// <summary>
        /// Activates the map tool to get map points from mouse clicks/movement
        /// </summary>
        /// <param name="obj"></param>
        internal void OnActivateTool(object obj)
        {
            SetToolActiveInToolBar(ArcMap.Application, MAP_TOOL_NAME);
        }
        /// <summary>
        /// Handler for the "Enter"key command
        /// Calls CreateMapElement
        /// </summary>
        /// <param name="obj"></param>
        internal virtual void OnEnterKeyCommand(object obj)
        {
            var depends = obj as System.Windows.DependencyObject;

            // check all children of dependency object for validation errors
            if (depends != null && !IsValid(depends))
                return;

            if (!CanCreateElement)
                return;

            var geom = CreateMapElement();

            if (geom != null)
            {
                ZoomToExtent(geom);
            }
        }

        /// <summary>
        /// Handler for opening the edit properties dialog
        /// </summary>
        /// <param name="obj"></param>
        private void OnEditPropertiesDialogCommand(object obj)
        {
            var dlg = new EditPropertiesView();
            dlg.DataContext = new EditPropertiesViewModel();

            dlg.ShowDialog();
        }

        private bool IsValid(System.Windows.DependencyObject obj)
        {
            // The dependency object is valid if it has no errors and all
            // of its children (that are dependency objects) are error-free.
            return !Validation.GetHasError(obj) &&
            System.Windows.LogicalTreeHelper.GetChildren(obj)
            .OfType<System.Windows.DependencyObject>()
            .All(IsValid);
        }

        /// <summary>
        /// Handler for the new map point click event
        /// </summary>
        /// <param name="obj">IPoint</param>
        internal virtual void OnNewMapPointEvent(object obj)
        {
            if ((ArcMap.Document == null) || (ArcMap.Document.FocusMap == null) 
                || !IsActiveTab)
                return;

            var point = obj as IPoint;

            if (!IsValidPoint(point))
            {
                MessageBox.Show(
                        DistanceAndDirectionLibrary.Properties.Resources.MsgOutOfAOI,
                        DistanceAndDirectionLibrary.Properties.Resources.DistanceDirectionLabel,
                        MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }

            if (point == null)
                return;

            if (!HasPoint1)
            {
                // clear temp graphics
                ClearTempGraphics();
                Point1 = point;
                HasPoint1 = true;

                var color = new RgbColorClass() { Green = 255 } as IColor;
                IDictionary<String, System.Object> ptAttributes = new Dictionary<String, System.Object>();
                ptAttributes.Add("X", Point1.X);
                ptAttributes.Add("Y", Point1.Y);
                AddGraphicToMap(Point1, color, true, attributes: ptAttributes);

                // lets try feedback
                IActiveView av = ArcMap.Document.FocusMap as IActiveView;
                CreateFeedback(point, av);
                feedback.Start(point);
            }
            else if (!HasPoint2)
            {
                ResetFeedback();
                Point2 = point;
                HasPoint2 = true;
                point2Formatted = string.Empty;
                RaisePropertyChanged(() => Point2Formatted);
            }

            if (HasPoint1 && HasPoint2)
            {
                CreateMapElement();
                ResetPoints();
            }

            if (!HasPoint3)
            {
                HasPoint3 = true;
            }
        }

        #endregion

        #region Public Functions
        /// <summary>
        /// Method used to deactivate tool
        /// </summary>
        public void DeactivateTool(string toolname)
        {
            if (ArcMap.Application != null
                && ArcMap.Application.CurrentTool != null
                && ArcMap.Application.CurrentTool.Name.Equals(toolname))
            {
                ArcMap.Application.CurrentTool = null;
            }
        }
        /// <summary>
        /// Method to set the map tool as the active tool for the map
        /// </summary>
        /// <param name="application"></param>
        /// <param name="toolName"></param>
        public void SetToolActiveInToolBar(ESRI.ArcGIS.Framework.IApplication application, System.String toolName)
        {
            ESRI.ArcGIS.Framework.ICommandBars commandBars = application.Document.CommandBars;
            ESRI.ArcGIS.esriSystem.UID commandID = new ESRI.ArcGIS.esriSystem.UIDClass();
            commandID.Value = toolName;
            ESRI.ArcGIS.Framework.ICommandItem commandItem = commandBars.Find(commandID, false, false);

            if (commandItem != null)
                application.CurrentTool = commandItem;
        }

        #endregion

        #region Private Functions

        private void ZoomToExtent(IGeometry geom)
        {
            if (geom == null || ArcMap.Document == null)
                return;

            var mxdoc = ArcMap.Document as IMxDocument;
            if (mxdoc == null)
                return;

            var av = mxdoc.FocusMap as IActiveView;
            if (av == null)
                return;

            IEnvelope env = geom.Envelope;

            double extentPercent = (env.XMax - env.XMin) > (env.YMax - env.YMin) ? (env.XMax - env.XMin) * .3 : (env.YMax - env.YMin) * .3;
            env.XMax = env.XMax + extentPercent;
            env.XMin = env.XMin - extentPercent;
            env.YMax = env.YMax + extentPercent;
            env.YMin = env.YMin - extentPercent;

            av.Extent = env;
            av.Refresh();
        }

        /// <summary>
        /// Method to check to see point is withing the map area of interest
        /// </summary>
        /// <param name="point">IPoint to validate</param>
        /// <returns></returns>
        internal bool IsValidPoint(IPoint point)
        {
            if ((point != null) && (ArcMap.Document != null) && 
                (ArcMap.Document.FocusMap != null))
            {
                return IsPointWithinExtent(point, UnionAllLayerExtents(ArcMap.Document.FocusMap));
            }
            return false;
        }

        /// <summary>
        /// Method used to check to see if a point is contained by an envelope
        /// </summary>
        /// <param name="point">IPoint</param>
        /// <param name="env">IEnvelope</param>
        /// <returns></returns>
        internal bool IsPointWithinExtent(IPoint point, IEnvelope env)
        {
            var relationOp = env as IRelationalOperator;

            if (relationOp == null)
                return false;

            return relationOp.Contains(point);
        }

        /// <summary>
        /// returns ILayer if found in the map layer collection
        /// </summary>
        /// <param name="map">IMap</param>
        /// <param name="name">string name of layer</param>
        /// <returns></returns>
        internal ILayer GetLayerFromMapByName(IMap map, string name)
        {
            var layers = map.get_Layers();
            var layer = layers.Next();

            while (layer != null)
            {
                if (layer.Name == name)
                    return layer;

                layer = layers.Next();
            }

            return null;
        }

        /// <summary>
        /// Returns the extent of a basemap
        /// </summary>
        /// <param name="map">IMap interface</param>
        /// <returns>IEnvelope interface</returns>
        internal IEnvelope GetBasemapExtent(IMap map)
        {
            try
            {
                var basemapList = new List<IEnvelope>();

                var layers = map.get_Layers();
                var layer = layers.Next();
                while (layer != null)
                {
                    if (layer is IBasemapLayer)
                        basemapList.Add(layer.AreaOfInterest);
                    layer = layers.Next();
                }
                if (basemapList.Count > 1) 
                    return basemapList.Aggregate((a,b) => ((IArea)a).Area > ((IArea)b).Area ? a : b);
                if (basemapList.Count == 1)
                    return basemapList[0];
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex.Message);
            }
            return null;
        }

        /// <summary>
        /// Checks if a input geometry is within an extent
        /// </summary>
        /// <param name="inputGeom">Input Geometry</param>
        /// <param name="searchExtent">Search Extent</param>
        /// <returns>bool</returns>
        internal bool IsGeometryWithinExtent(IGeometry inputGeom, IEnvelope searchExtent)
        {
            if (inputGeom != null && searchExtent != null)
            {
                return ((IRelationalOperator)inputGeom).Within(searchExtent);
            }
            return true;
        }
        
        /// <summary>
        /// Creates a circle geometry based on center point and radius
        /// </summary>
        /// <param name="centerPoint">IPoint - Center Point </param>
        /// <param name="radius">double - Radius</param>
        /// <returns>IGeometry</returns>
        internal IGeometry CreateCircleGeometry(IPoint centerPoint, double radius)
        {
            var circularArc = new CircularArcClass();
            var construtionCircularArc = (IConstructCircularArc)circularArc;
            construtionCircularArc.ConstructCircle(centerPoint, radius, true);
            var seg = (ISegment)construtionCircularArc;
            var segCollection = new PolylineClass();
            segCollection.AddSegment(seg);
            var geom = (IGeometry)segCollection;
            return geom;
        }

        /// <summary>
        /// Unions all extents 
        /// </summary>
        /// <param name="map"></param>
        /// <returns>envelope</returns>
        internal IEnvelope UnionAllLayerExtents(IMap map)
        {
            var layers = map.get_Layers();
            var layer = layers.Next();

            var geomBag = new GeometryBagClass();
            geomBag.SpatialReference = map.SpatialReference;

            var geomColl = (IGeometryCollection)geomBag;
            object MissingType = Type.Missing;

            while (layer != null)
            {
                geomColl.AddGeometry(layer.AreaOfInterest, ref MissingType, ref MissingType);
                layer = layers.Next();
            }

            return geomBag.Envelope;
        }

        /// <summary>
        /// Method will return a formatted point as a string based on the configuration settings for display coordinate type
        /// </summary>
        /// <param name="point">IPoint that is to be formatted</param>
        /// <returns>String that is formatted based on addin config display coordinate type</returns>
        private string GetFormattedPoint(IPoint point)
        {
            if (point == null)
                return "NA";

            var result = string.Format("{0:0.0} {1:0.0}", point.Y, point.X);
            var cn = point as IConversionNotation;
            if (cn != null)
            {
                try
                {
                    switch (DistanceAndDirectionConfig.AddInConfig.DisplayCoordinateType)
                    {
                        case CoordinateTypes.DD:
                            result = cn.GetDDFromCoords(6);
                            break;
                        case CoordinateTypes.DDM:
                            result = cn.GetDDMFromCoords(4);
                            break;
                        case CoordinateTypes.DMS:
                            result = cn.GetDMSFromCoords(2);
                            break;
                        //case CoordinateTypes.GARS:
                        //    result = cn.GetGARSFromCoords();
                        //    break;
                        case CoordinateTypes.MGRS:
                            result = cn.CreateMGRS(5, true, esriMGRSModeEnum.esriMGRSMode_Automatic);
                            break;
                        case CoordinateTypes.USNG:
                            result = cn.GetUSNGFromCoords(5, true, true);
                            break;
                        case CoordinateTypes.UTM:
                            result = cn.GetUTMFromCoords(esriUTMConversionOptionsEnum.esriUTMAddSpaces | esriUTMConversionOptionsEnum.esriUTMUseNS);
                            break;
                        default:
                            break;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(ex.Message);
                }
            }
            return result;
        }
        /// <summary>
        /// Method used to totally reset the tool
        /// reset points, feedback
        /// clear out textboxes
        /// </summary>
        internal virtual void Reset(bool toolReset)
        {
            if (toolReset)
            {
                DeactivateTool("Esri_ArcMapAddinDistanceAndDirection_MapPointTool");
            }

            ResetPoints();
            Point1 = null;
            Point2 = null;
            Point1Formatted = string.Empty;
            Point2Formatted = string.Empty;

            ResetFeedback();

            Distance = 0.0;

            ClearTempGraphics();
        }
        /// <summary>
        /// Resets Points 1 and 2
        /// </summary>
        internal virtual void ResetPoints()
        {
            HasPoint1 = HasPoint2 = HasPoint3 = false;
        }

        /// <summary>
        /// Resets feedback aka cancels feedback
        /// </summary>
        internal void ResetFeedback()
        {
            if (feedback == null)
                return;

            feedback.Stop();
            feedback = null;

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        /// <summary>
        /// Handler for the tab item selected event
        /// Helps keep track of which tab item/viewmodel is active
        /// </summary>
        /// <param name="obj">bool if selected or not</param>
        private void OnTabItemSelected(object obj)
        {
            if (obj == null)
                return;
            IsToolActive = false;
            IsActiveTab = (obj == this);
        }

        /// <summary>
        /// Revalidate Point2Formatted when items in the TOC changed
        /// </summary>
        private void onActiveViewChagedEvent(object obj)
        {
            if (ArcMap.LayerCount > 0)
            {
                RaisePropertyChanged(() => Point2Formatted);
            }
        }

        /// <summary>
        /// Handler for the escape key press event
        /// Helps cancel operation when escape key is pressed
        /// </summary>
        /// <param name="obj">always null</param>
        private void OnKeypressEscape(object obj)
        {
            if (isActiveTab)
            {
                if (ArcMap.Application.CurrentTool != null)
                {
                    // Special handling required for ellipses
                    if (this is EllipseViewModel)
                    {
                        // User has activated the Map Point tool but not created a point
                        // Or User has previously finished creating a graphic
                        // Either way, assume they want to disable the Map Point tool
                        if ((IsToolActive && !HasPoint1) || (IsToolActive && HasPoint3))
                        {
                            Reset(true);
                            IsToolActive = false;
                            return;
                        }

                        // User has activated Map Point tool and created a point but not completed the graphic
                        // Assume they want to cancel any graphic creation in progress 
                        // but still keep the Map Point tool active
                        if (IsToolActive && HasPoint1 && !HasPoint3)
                        {
                            Reset(false);
                            return;
                        }
                    }
                    else
                    {
                        // User has activated the Map Point tool but not created a point
                        // Or User has previously finished creating a graphic
                        // Either way, assume they want to disable the Map Point tool
                        if ((IsToolActive && !HasPoint1) || (IsToolActive && HasPoint2))
                        {
                            Reset(true);
                            IsToolActive = false;
                            return;
                        }

                        // User has activated Map Point tool and created a point but not completed the graphic
                        // Assume they want to cancel any graphic creation in progress 
                        // but still keep the Map Point tool active
                        if (IsToolActive && HasPoint1 && !HasPoint2)
                        {
                            Reset(false);
                            return;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Handler for when key is manually pressed in a Point Text Box
        /// </summary>
        /// <param name="obj">always null</param>
        private void OnPointTextBoxKeyDown(object obj)
        {
            if (isActiveTab)
            {
                // deactivate the map point tool when a point is manually entered
                if (IsToolActive)
                    IsToolActive = false;
            }
        }

        /// <summary>
        /// Converts a polyline into a polygon
        /// </summary>
        /// <param name="line">IPolyline</param>
        /// <returns>IPolygon</returns>
        internal IPolygon PolylineToPolygon(IPolyline line)
        {
            try
            {
                var geomCol = (IGeometryCollection)new Polygon();
                var polylineGeoms = (IGeometryCollection)line;
                for (var i = 0; i < polylineGeoms.GeometryCount; i++)
                {
                    var ringSegs = (ISegmentCollection)new RingClass();
                    ringSegs.AddSegmentCollection((ISegmentCollection)polylineGeoms.Geometry[i]);
                    geomCol.AddGeometry((IGeometry)ringSegs);
                }
                var newPoly = (IPolygon)geomCol;
                newPoly.SimplifyPreserveFromTo();
                return newPoly;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Adds graphics text to the map graphics container
        /// </summary>
        /// <param name="geom">IGeometry</param>
        /// <param name="text">string</param>
        internal void AddTextToMap(IGeometry geom, string text)
        {
            if ((ArcMap.Application == null) || (ArcMap.Application.Document == null))
                return;

            var mxDoc = ArcMap.Application.Document as IMxDocument;

            if ((mxDoc == null) || (mxDoc.FocusMap == null))
                return;

            var av = mxDoc.FocusMap as IActiveView;
            var gc = av as IGraphicsContainer;

            if (gc == null)
                return;

            var textEle = (ITextElement)new TextElement();
            textEle.Text = text;
            var elem = (IElement)textEle;
            elem.Geometry = geom;

            var eprop = (IElementProperties)elem;
            eprop.Name = Guid.NewGuid().ToString();

            if (geom.GeometryType == esriGeometryType.esriGeometryPoint)
                GraphicsList.Add(new Graphic(GraphicTypes.Point, eprop.Name, geom, this, false));
            else if (this is LinesViewModel)
                GraphicsList.Add(new Graphic(GraphicTypes.Line, eprop.Name, geom, this, false));
            else if (this is CircleViewModel)
                GraphicsList.Add(new Graphic(GraphicTypes.Circle, eprop.Name, geom, this, false));
            else if (this is EllipseViewModel)
                GraphicsList.Add(new Graphic(GraphicTypes.Ellipse, eprop.Name, geom, this, false));
            else if (this is RangeViewModel)
                GraphicsList.Add(new Graphic(GraphicTypes.RangeRing, eprop.Name, geom, this, false));

            gc.AddElement(elem, 0);
            av.PartialRefresh(esriViewDrawPhase.esriViewGraphics, null, null);

            RaisePropertyChanged(() => HasMapGraphics);
        }

        internal void AddTextToMap(IGeometry geom, string text, double angle, AzimuthTypes azimuthType, bool hasRotation = true)
        {
            if ((ArcMap.Application == null) || (ArcMap.Application.Document == null))
                return;

            var mxDoc = ArcMap.Application.Document as IMxDocument;

            if ((mxDoc == null) || (mxDoc.FocusMap == null))
                return;

            var av = mxDoc.FocusMap as IActiveView;
            var gc = av as IGraphicsContainer;

            if (gc == null)
                return;

            double bearing = (azimuthType == AzimuthTypes.Mils) ? angle * 0.05625 : angle;

            double rotate = 360 - (bearing + 270.0) % 360;
            if (rotate > 90 && rotate <= 270)
                rotate = rotate - 180;
            var textEle = (ITextElement)new TextElement();
            textEle.Text = text;
            ITextSymbol tsym = new TextSymbol();

            tsym.Angle = (hasRotation) ? rotate : 0;
            if (!hasRotation)
                tsym.HorizontalAlignment = esriTextHorizontalAlignment.esriTHALeft;
            textEle.Symbol = tsym;
            var elem = (IElement)textEle;
            elem.Geometry = geom;

            var eprop = (IElementProperties)elem;
            eprop.Name = Guid.NewGuid().ToString();
            Dictionary<String, Double> attributeMap = new Dictionary<string, double>();
            if (geom.GeometryType == esriGeometryType.esriGeometryPoint)
                GraphicsList.Add(new Graphic(GraphicTypes.Point, eprop.Name, geom, this, false));
            else if (this is LinesViewModel)
                GraphicsList.Add(new Graphic(GraphicTypes.Line, eprop.Name, geom, this, false));
            else if (this is CircleViewModel)
                GraphicsList.Add(new Graphic(GraphicTypes.Circle, eprop.Name, geom, this, false));
            else if (this is EllipseViewModel)
                GraphicsList.Add(new Graphic(GraphicTypes.Ellipse, eprop.Name, geom, this, false));
            else if (this is RangeViewModel)
                GraphicsList.Add(new Graphic(GraphicTypes.RangeRing, eprop.Name, geom, this, false));

            gc.AddElement(elem, 0);
            av.PartialRefresh(esriViewDrawPhase.esriViewGraphics, null, null);

            RaisePropertyChanged(() => HasMapGraphics);
        }

        /// <summary>
        /// Adds a graphic element to the map graphics container
        /// </summary>
        /// <param name="geom">IGeometry</param>
        internal void AddGraphicToMap(IGeometry geom, IColor color, bool IsTempGraphic = false, esriSimpleMarkerStyle markerStyle = esriSimpleMarkerStyle.esriSMSCircle, esriRasterOpCode rasterOpCode = esriRasterOpCode.esriROPNOP, IDictionary<String, System.Object> attributes = null)
        {
            if (geom == null || ArcMap.Document == null || ArcMap.Document.FocusMap == null)
                return;

            //Check if the geometry exists in the graphics list. 
            //If so, then exit
            if (GraphicsList.Any(g => ((IRelationalOperator)g.Geometry).Equals(geom) && g.GraphicType == GraphicTypes.Line))
                return;

            IElement element = null;
            double width = 2.0;

            geom.Project(ArcMap.Document.FocusMap.SpatialReference);

            if (geom.GeometryType == esriGeometryType.esriGeometryPoint)
            {
                // Marker symbols
                var simpleMarkerSymbol = (ISimpleMarkerSymbol)new SimpleMarkerSymbol();
                simpleMarkerSymbol.Color = color;
                simpleMarkerSymbol.Outline = true;
                simpleMarkerSymbol.OutlineColor = color;
                simpleMarkerSymbol.Size = 5;
                simpleMarkerSymbol.Style = markerStyle;

                var markerElement = (IMarkerElement)new MarkerElement();
                markerElement.Symbol = simpleMarkerSymbol;
                element = (IElement)markerElement;
            }
            else if (geom.GeometryType == esriGeometryType.esriGeometryPolyline)
            {
                // create graphic then add to map
                ILineSymbol lineSymbol;
                if (this is LinesViewModel)
                {
                    lineSymbol = new CartographicLineSymbolClass();

                    ISimpleLineDecorationElement simpleLineDecorationElement = new SimpleLineDecorationElementClass();
                    simpleLineDecorationElement.AddPosition(1);
                    simpleLineDecorationElement.MarkerSymbol = new ArrowMarkerSymbolClass()
                    {
                        Color = color,
                        Size = 6,
                        Length = 8,
                        Width = 6,
                        XOffset = 0.8
                    };

                    ILineDecoration lineDecoration = new LineDecorationClass();
                    lineDecoration.AddElement(simpleLineDecorationElement);

                    ((ILineProperties)lineSymbol).LineDecoration = lineDecoration;
                }
                else
                {
                    lineSymbol = new SimpleLineSymbolClass();
                }

                lineSymbol.Color = color;
                lineSymbol.Width = width;

                if (IsTempGraphic && rasterOpCode != esriRasterOpCode.esriROPNOP)
                {
                    lineSymbol.Width = 1;
                    ((ISymbol)lineSymbol).ROP2 = rasterOpCode;
                }

                var le = (ILineElement)new LineElementClass();
                element = (IElement)le;
                le.Symbol = lineSymbol;
            }

            if (element == null)
                return;

            IClone clone = geom as IClone;
            element.Geometry = clone as IGeometry;

            var mxdoc = ArcMap.Application.Document as IMxDocument;
            var av = mxdoc.FocusMap as IActiveView;
            var gc = av as IGraphicsContainer;

            if (gc == null)
                return;

            // store guid
            var eprop = (IElementProperties)element;
            eprop.Name = Guid.NewGuid().ToString();

            if (geom.GeometryType == esriGeometryType.esriGeometryPoint)
                GraphicsList.Add(new Graphic(GraphicTypes.Point, eprop.Name, geom, this, IsTempGraphic, attributes: attributes));
            else if (this is LinesViewModel)
                GraphicsList.Add(new Graphic(GraphicTypes.Line, eprop.Name, geom, this, IsTempGraphic, attributes: attributes));
            else if (this is CircleViewModel)
                GraphicsList.Add(new Graphic(GraphicTypes.Circle, eprop.Name, geom, this, IsTempGraphic, attributes: attributes));
            else if (this is EllipseViewModel)
                GraphicsList.Add(new Graphic(GraphicTypes.Ellipse, eprop.Name, geom, this, IsTempGraphic, attributes: attributes));
            else if (this is RangeViewModel)
                GraphicsList.Add(new Graphic(GraphicTypes.RangeRing, eprop.Name, geom, this, IsTempGraphic, attributes: attributes));

            gc.AddElement(element, 0);

            //refresh map
            av.PartialRefresh(esriViewDrawPhase.esriViewGraphics, null, null);

            if (!IsTempGraphic)
                RaisePropertyChanged(() => HasMapGraphics);
        }
        /// <summary>
        /// Adds a graphic to the active view/map graphics container, default color is RED
        /// </summary>
        /// <param name="geom"></param>
        /// <param name="IsTempGraphic"></param>
        internal void AddGraphicToMap(IGeometry geom, bool IsTempGraphic = false, IDictionary<String, Double> attributes = null)
        {
            var color = new RgbColorClass() { Red = 255 } as IColor;
            AddGraphicToMap(geom, color, IsTempGraphic);
        }
        internal ISpatialReferenceFactory3 srf3 = null;
        /// <summary>
        /// Gets the linear unit from the esri constants for linear units
        /// </summary>
        /// <returns>ILinearUnit</returns>
        internal ILinearUnit GetLinearUnit()
        {
            int unitType = (int)esriSRUnitType.esriSRUnit_Meter;
            if (srf3 == null)
            {
                Type srType = Type.GetTypeFromProgID("esriGeometry.SpatialReferenceEnvironment");
                srf3 = Activator.CreateInstance(srType) as ISpatialReferenceFactory3;
            }

            if (srf3 == null)
                return new LinearUnitClass();

            switch (LineDistanceType)
            {
                case DistanceTypes.Feet:
                    unitType = (int)esriSRUnitType.esriSRUnit_Foot;
                    break;
                case DistanceTypes.Kilometers:
                    unitType = (int)esriSRUnitType.esriSRUnit_Kilometer;
                    break;
                case DistanceTypes.Meters:
                    unitType = (int)esriSRUnitType.esriSRUnit_Meter;
                    break;
                case DistanceTypes.Miles:
                    unitType = (int)esriSRUnitType.esriSRUnit_SurveyMile;
                    break;
                case DistanceTypes.NauticalMiles:
                    unitType = (int)esriSRUnitType.esriSRUnit_NauticalMile;
                    break;
                case DistanceTypes.Yards:
                    unitType = (int)esriSRUnit2Type.esriSRUnit_InternationalYard;
                    break;
                default:
                    unitType = (int)esriSRUnitType.esriSRUnit_Meter;
                    break;
            }

            return srf3.CreateUnit(unitType) as ILinearUnit;
        }

        internal double ConvertFromTo(DistanceTypes fromType, DistanceTypes toType, double input)
        {
            double result = 0.0;

            var converter = (IUnitConverter)new UnitConverterClass();

            result = converter.ConvertUnits(input, GetEsriUnit(fromType), GetEsriUnit(toType));

            return result;
        }

        // Overload for calling from another class where lineDistanceType is not available
        protected double TrimPrecision(double inputDistance, bool lax)
        {
            return TrimPrecision(inputDistance, lineDistanceType, lax);
        }

        // Remove superfluous precision
        protected double TrimPrecision(double inputDistance, DistanceTypes lineDistanceType_param, bool lax)
        {
            int largeUnitRoundingFactor = 4;
            int smallUnitRoundingFactor = 2;

            // We have a less strict mode for trimming precision for the case that the user
            // has Distance Calculator expanded and thus might have a large unit selected
            // - otherwise we can trim label down to e.g. 0.00 Miles
            if (lax)
            {
                largeUnitRoundingFactor = 6;
                smallUnitRoundingFactor = 2;
            }

            double returnDistance = 0;
            // For smaller units assume a tenth is sufficient
            // For larger units provide ten thousandth i.e. 4 decimal places, probably more than sufficient
            switch (lineDistanceType_param)
            {
                case DistanceTypes.Kilometers:
                case DistanceTypes.Miles:
                case DistanceTypes.NauticalMiles:
                    returnDistance = Math.Round(inputDistance, largeUnitRoundingFactor);
                    break;
                case DistanceTypes.Meters:
                case DistanceTypes.Feet:
                case DistanceTypes.Yards:
                    returnDistance = Math.Round(inputDistance, smallUnitRoundingFactor);
                    break;
                default:
                    break;
            }
            return returnDistance;
        }

        private esriUnits GetEsriUnit(DistanceTypes distanceType)
        {
            esriUnits unit = esriUnits.esriMeters;

            switch (distanceType)
            {
                case DistanceTypes.Feet:
                    unit = esriUnits.esriFeet;
                    break;
                case DistanceTypes.Kilometers:
                    unit = esriUnits.esriKilometers;
                    break;
                case DistanceTypes.Meters:
                    unit = esriUnits.esriMeters;
                    break;
                case DistanceTypes.Miles:
                    unit = esriUnits.esriMiles;
                    break;
                case DistanceTypes.NauticalMiles:
                    unit = esriUnits.esriNauticalMiles;
                    break;
                case DistanceTypes.Yards:
                    unit = esriUnits.esriYards;
                    break;
                default:
                    unit = esriUnits.esriMeters;
                    break;
            }

            return unit;
        }

        /// <summary>
        /// Get the currently selected geodetic type
        /// </summary>
        /// <returns>esriGeodeticType</returns>
        internal esriGeodeticType GetEsriGeodeticType()
        {
            esriGeodeticType type = esriGeodeticType.esriGeodeticTypeGeodesic;

            switch (LineType)
            {
                case LineTypes.Geodesic:
                    type = esriGeodeticType.esriGeodeticTypeGeodesic;
                    break;
                case LineTypes.GreatElliptic:
                    type = esriGeodeticType.esriGeodeticTypeGreatElliptic;
                    break;
                case LineTypes.Loxodrome:
                    type = esriGeodeticType.esriGeodeticTypeLoxodrome;
                    break;
                default:
                    type = esriGeodeticType.esriGeodeticTypeGeodesic;
                    break;
            }

            return type;
        }
        internal double GetGeodeticLengthFromPolyline(IPolyline polyline)
        {
            if (polyline == null)
                return 0.0;

            var polycurvegeo = polyline as IPolycurveGeodetic;
            if (polycurvegeo == null)
                return 0.0;

            var geodeticType = GetEsriGeodeticType();
            var linearUnit = GetLinearUnit();
            var geodeticLength = polycurvegeo.get_LengthGeodetic(geodeticType, linearUnit);

            return geodeticLength;
        }

        /// <summary>
        /// Gets the distance/lenght of a polyline
        /// </summary>
        /// <param name="geometry">IGeometry</param>
        internal void UpdateDistance(IGeometry geometry)
        {
            var polyline = geometry as IPolyline;

            if (polyline == null)
                return;

            double rawDistance = GetGeodeticLengthFromPolyline(polyline);
            // Round the superfluous precision appropriately to unit
            Distance = TrimPrecision(rawDistance, lineDistanceType, false);
        }

        /// <summary>
        /// Handler for the mouse move event
        /// When the mouse moves accross the map, IPoints are returned to aid in updating feedback to user
        /// </summary>
        /// <param name="obj">IPoint</param>
        internal virtual void OnMouseMoveEvent(object obj)
        {
            if (!IsActiveTab)
                return;

            var point = obj as IPoint;

            if (point == null)
                return;

            // dynamically update start point if not set yet
            if (!HasPoint1)
            {
                Point1 = point;
            }
            else if (HasPoint1 && !HasPoint2)
            {
                Point2Formatted = string.Empty;
                Point2 = point;
                // get distance from feedback
                var polyline = GetGeoPolylineFromPoints(Point1, point);
                UpdateDistance(polyline);
            }

            // update feedback
            if (HasPoint1 && !HasPoint2)
            {
                FeedbackMoveTo(point);
            }
        }
        /// <summary>
        /// Gets a geodetic polyline from two points
        /// startPoint is where it will restart from
        /// endPoint is where you want it to end for the return of the polyline
        /// </summary>
        /// <param name="startPoint">startPoint is where it will restart from</param>
        /// <param name="endPoint">endPoint is where you want it to end for the return of the polyline</param>
        /// <returns>IPolyline</returns>
        internal IPolyline GetGeoPolylineFromPoints(IPoint startPoint, IPoint endPoint)
        {
            var construct = new Polyline() as IConstructGeodetic;
            if (construct == null)
                return null;

            construct.ConstructGeodeticLineFromPoints(GetEsriGeodeticType(), startPoint, endPoint, GetLinearUnit(), esriCurveDensifyMethod.esriCurveDensifyByDeviation, -1.0);

            return construct as IPolyline;
        }

        /// <summary>
        /// Creates a new geodetic line feedback to visualize the line to the user
        /// </summary>
        /// <param name="point">IPoint, start point</param>
        /// <param name="av">The current active view</param>
        internal void CreateFeedback(IPoint point, IActiveView av)
        {
            if ((av == null) || (point == null))
                return;

            ResetFeedback();
            feedback = new NewLineFeedback();
            var geoFeedback = (IGeodeticLineFeedback)feedback;
            geoFeedback.GeodeticConstructionMethod = GetEsriGeodeticType();
            geoFeedback.UseGeodeticConstruction = true;
            geoFeedback.SpatialReference = point.SpatialReference;
            var displayFB = (IDisplayFeedback)feedback;
            displayFB.Display = av.ScreenDisplay;
        }
 
        /// <summary>
        /// Method used to convert a string to a known coordinate
        /// Assumes WGS84 for now
        /// Uses the IConversionNotation interface
        /// </summary>
        /// <param name="coordinate">the coordinate as a string</param>
        /// <returns>IPoint if successful, null if not</returns>
        internal IPoint GetPointFromString(string coordinate)
        {
            Type t = Type.GetTypeFromProgID("esriGeometry.SpatialReferenceEnvironment");
            System.Object obj = Activator.CreateInstance(t);
            ISpatialReferenceFactory srFact = obj as ISpatialReferenceFactory;

            if (srFact == null)
                return null;

            // Use the enumeration to create an instance of the predefined object
            IGeographicCoordinateSystem geographicCS =
                srFact.CreateGeographicCoordinateSystem((int)
                esriSRGeoCSType.esriSRGeoCS_WGS1984);

            var point = (IPoint)new Point();

            point.SpatialReference = geographicCS;
            var cn = point as IConversionNotation;

            if (cn == null)
                return null;

            try { cn.PutCoordsFromDD(coordinate); return point; } catch { }
            try { cn.PutCoordsFromDDM(coordinate); return point; } catch { }
            try { cn.PutCoordsFromDMS(coordinate); return point; } catch { }
            try { cn.PutCoordsFromGARS(esriGARSModeEnum.esriGARSModeCENTER, coordinate); return point; } catch { }
            try { cn.PutCoordsFromGARS(esriGARSModeEnum.esriGARSModeLL, coordinate); return point; } catch { }
            try { cn.PutCoordsFromMGRS(coordinate, esriMGRSModeEnum.esriMGRSMode_Automatic); return point; } catch { }
            try { cn.PutCoordsFromMGRS(coordinate, esriMGRSModeEnum.esriMGRSMode_NewStyle); return point; } catch { }
            try { cn.PutCoordsFromMGRS(coordinate, esriMGRSModeEnum.esriMGRSMode_NewWith180InZone01); return point; } catch { }
            try { cn.PutCoordsFromMGRS(coordinate, esriMGRSModeEnum.esriMGRSMode_OldStyle); return point; } catch { }
            try { cn.PutCoordsFromMGRS(coordinate, esriMGRSModeEnum.esriMGRSMode_OldWith180InZone01); return point; } catch { }
            try { cn.PutCoordsFromMGRS(coordinate, esriMGRSModeEnum.esriMGRSMode_USNG); return point; } catch { }
            try { cn.PutCoordsFromUSNG(coordinate); return point; } catch { }
            try { cn.PutCoordsFromUTM(esriUTMConversionOptionsEnum.esriUTMAddSpaces, coordinate); return point; } catch { }
            try { cn.PutCoordsFromUTM(esriUTMConversionOptionsEnum.esriUTMUseNS, coordinate); return point; } catch { }
            try { cn.PutCoordsFromUTM(esriUTMConversionOptionsEnum.esriUTMAddSpaces | esriUTMConversionOptionsEnum.esriUTMUseNS, coordinate); return point; } catch { }
            try { cn.PutCoordsFromUTM(esriUTMConversionOptionsEnum.esriUTMNoOptions, coordinate); return point; } catch { }
            try { cn.PutCoordsFromGeoRef(coordinate); return point; } catch { }

            // lets see if we have a PCS coordinate
            // we'll assume the same units as the map units
            // get spatial reference of map
            if (ArcMap.Document == null || ArcMap.Document.FocusMap == null || ArcMap.Document.FocusMap.SpatialReference == null)
                return null;

            var map = ArcMap.Document.FocusMap;
            var pcs = map.SpatialReference as IProjectedCoordinateSystem;

            if (pcs == null)
                return null;

            point.SpatialReference = map.SpatialReference;
            // get pcs coordinate from input
            coordinate = coordinate.Trim();

            Regex regexMercator = new Regex(@"^(?<latitude>\-?\d+\.?\d*)[+,;:\s]*(?<longitude>\-?\d+\.?\d*)");

            var matchMercator = regexMercator.Match(coordinate);

            if (matchMercator.Success && matchMercator.Length == coordinate.Length)
            {
                try
                {
                    var Lat = Double.Parse(matchMercator.Groups["latitude"].Value);
                    var Lon = Double.Parse(matchMercator.Groups["longitude"].Value);
                    point.PutCoords(Lon, Lat);
                    return point;
                }
                catch
                {
                    return null;
                }
            }

            return null;
        }

        /// <summary>
        /// Method to use when you need to move a feedback line to a point
        /// This forces a new point to be used, sometimes this method projects the point to a different spatial reference
        /// </summary>
        /// <param name="point"></param>
        internal void FeedbackMoveTo(IPoint point)
        {
            if (feedback == null || point == null)
                return;

            feedback.MoveTo(new Point() { X = point.X, Y = point.Y, SpatialReference = point.SpatialReference });
        }
        #endregion Private Functions

    }
}
