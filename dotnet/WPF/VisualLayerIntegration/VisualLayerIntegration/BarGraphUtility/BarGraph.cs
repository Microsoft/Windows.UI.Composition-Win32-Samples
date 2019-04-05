﻿//  ---------------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// 
//  The MIT License (MIT)
// 
//  Permission is hereby granted, free of charge, to any person obtaining a copy
//  of this software and associated documentation files (the "Software"), to deal
//  in the Software without restriction, including without limitation the rights
//  to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
//  copies of the Software, and to permit persons to whom the Software is
//  furnished to do so, subject to the following conditions:
// 
//  The above copyright notice and this permission notice shall be included in
//  all copies or substantial portions of the Software.
// 
//  THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
//  IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
//  FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
//  AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
//  LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
//  OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
//  THE SOFTWARE.
//  ---------------------------------------------------------------------------------

using SharpDX;
using SharpDX.Direct2D1;
using SharpDX.DirectWrite;
using System;
using System.Collections;
using System.Collections.Generic;
using Windows.UI;
using Windows.UI.Composition;
using SysWin = System.Windows;

namespace BarGraphUtility
{
    class BarGraph
    {
        private double[] _graphData;

        private Compositor _compositor;
        private IntPtr _hwnd;

        private float _graphWidth, _graphHeight;
        private float _shapeGraphContainerHeight, _shapeGraphContainerWidth, _shapeGraphOffsetY, _shapeGraphOffsetX;
        private float _barWidth, _barSpacing;
        private double _maxBarValue;

        private GraphBarStyle _graphBarStyle;
        private Windows.UI.Color[] _graphBarColors;

        private Dictionary<int,Bar> _barValueMap;

        private WindowRenderTarget _textRenderTarget;
        private SolidColorBrush _textSceneColorBrush;
        private TextFormat _textFormatTitle;
        private TextFormat _textFormatHorizontal;
        private TextFormat _textFormatVertical;

        private ShapeVisual _shapeContainer;
        private CompositionLineGeometry _xAxisLine;
        private CompositionLineGeometry _yAxisLine;
        private ContainerVisual _mainContainer;

        private static SharpDX.Mathematics.Interop.RawColor4 _black = new SharpDX.Mathematics.Interop.RawColor4(0, 0, 0, 255);
        private static SharpDX.Mathematics.Interop.RawColor4 _white = new SharpDX.Mathematics.Interop.RawColor4(255, 255, 255, 255);

        private static float _textSize = 20.0f;

        private AmbientLight _ambientLight;
        private SpotLight _barOutlineLight;
        private PointLight _barLight;

        public string Title { get; set; }
        public string XAxisLabel { get; set; }
        public string YAxisLabel { get; set; }
        public ContainerVisual BarRoot { get; }
        public ContainerVisual GraphRoot { get; }

        public enum GraphBarStyle
        {
            Single = 0,
            Random = 1,
            PerBarLinearGradient = 3,
            AmbientAnimatingPerBarLinearGradient = 4
        }

        // Constructor for bar graph.
        // To insert graph, call the constructor then use barGraph.Root to get the container to parent.
        public BarGraph(Compositor compositor, 
            IntPtr hwnd, 
            string title, 
            string xAxisLabel, 
            string yAxisLabel, 
            float width, 
            float height, 
            double dpiX, 
            double dpiY, 
            double[] data, 
            WindowRenderTarget renderTarget, 
            bool AnimationsOn = true, 
            GraphBarStyle graphBarStyle = GraphBarStyle.Single,
            Windows.UI.Color[] barColors = null)
        {
            _compositor = compositor;
            _hwnd = hwnd;
            _graphWidth = (float)(width * dpiX / 96.0);
            _graphHeight = (float)(height * dpiY / 96.0);

            _graphData = data;

            Title = title;
            XAxisLabel = xAxisLabel;
            YAxisLabel = yAxisLabel;

            _graphBarStyle = graphBarStyle;

            _graphBarColors = barColors ?? new Windows.UI.Color[] { Colors.Blue };

            // Configure options for text.
            var factory2D = new SharpDX.Direct2D1.Factory();

            var properties = new HwndRenderTargetProperties();
            properties.Hwnd = _hwnd;
            properties.PixelSize = new Size2((int)(width * dpiX / 96.0), (int)(width * dpiY / 96.0));
            properties.PresentOptions = PresentOptions.None;

            _textRenderTarget = renderTarget;

            // Generate graph structure.
            GraphRoot = GenerateGraphStructure();

            BarRoot = _compositor.CreateContainerVisual();
            GraphRoot.Children.InsertAtBottom(BarRoot);

            // If data has been provided, initialize bars and animations; otherwise, leave graph empty.
            if (_graphData.Length > 0)
            {
                _barValueMap = new Dictionary<int, Bar>();
                var bars = CreateBars(_graphData);
                AddBarsToTree(bars);
            }
        }

        private void UpdateSizeAndPositions()
        {
            _shapeGraphOffsetY = _graphHeight * 1 / 15;
            _shapeGraphOffsetX = _graphWidth * 1 / 15;
            _shapeGraphContainerHeight = _graphHeight - _shapeGraphOffsetY * 2;
            _shapeGraphContainerWidth = _graphWidth - _shapeGraphOffsetX * 2;

            _mainContainer.Offset = new System.Numerics.Vector3(_shapeGraphOffsetX, _shapeGraphOffsetY, 0);

            _barWidth = ComputeBarWidth();
            _barSpacing = (float)(0.5 * _barWidth);

            _shapeContainer.Offset = new System.Numerics.Vector3(_shapeGraphOffsetX, _shapeGraphOffsetY, 0);
            _shapeContainer.Size = new System.Numerics.Vector2(_shapeGraphContainerWidth, _shapeGraphContainerHeight);

            _xAxisLine.Start = new System.Numerics.Vector2(0, _shapeGraphContainerHeight - _shapeGraphOffsetY);
            _xAxisLine.End = new System.Numerics.Vector2(_shapeGraphContainerWidth - _shapeGraphOffsetX, _shapeGraphContainerHeight - _shapeGraphOffsetY);

            _yAxisLine.Start = new System.Numerics.Vector2(0, _shapeGraphContainerHeight - _shapeGraphOffsetY);
            _yAxisLine.End = new System.Numerics.Vector2(0, 0);
        }

        private ContainerVisual GenerateGraphStructure()
        {
            _mainContainer = _compositor.CreateContainerVisual();

            // Create shape tree to hold.
            _shapeContainer = _compositor.CreateShapeVisual();

            _xAxisLine = _compositor.CreateLineGeometry();
            _yAxisLine = _compositor.CreateLineGeometry();

            var xAxisShape = _compositor.CreateSpriteShape(_xAxisLine);
            xAxisShape.StrokeBrush = _compositor.CreateColorBrush(Colors.Black);
            xAxisShape.FillBrush = _compositor.CreateColorBrush(Colors.Black);

            var yAxisShape = _compositor.CreateSpriteShape(_yAxisLine);
            yAxisShape.StrokeBrush = _compositor.CreateColorBrush(Colors.Black);

            _shapeContainer.Shapes.Add(xAxisShape);
            _shapeContainer.Shapes.Add(yAxisShape);

            _mainContainer.Children.InsertAtTop(_shapeContainer);

            UpdateSizeAndPositions();

            // Draw text.
            DrawText(_textRenderTarget, Title, XAxisLabel, YAxisLabel, _textSize);

            // Return root node for graph.
            return _mainContainer;
        }

        public void UpdateSize(SysWin.DpiScale dpi, double newWidth, double newHeight)
        {
            var newDpiX = dpi.PixelsPerInchX;
            var newDpiY = dpi.PixelsPerInchY;

            var oldHeight = _graphHeight;
            var oldWidth = _graphWidth;
            _graphHeight = (float)(newWidth * newDpiY / 96.0);
            _graphWidth = (float)(newHeight * newDpiX / 96.0);

            UpdateSizeAndPositions();

            // Update bars.
            for (int i = 0; i < _barValueMap.Count; i++)
            {
                var bar = _barValueMap[i];

                var xOffset = _shapeGraphOffsetX + _barSpacing + (_barWidth + _barSpacing) * i;
                var height = bar.Height;
                if (oldHeight != newHeight)
                {
                    height = (float)GetAdjustedBarHeight(_maxBarValue, _graphData[i]);
                }

                bar.UpdateSize(_barWidth, height);
                bar.Root.Offset = new System.Numerics.Vector3(xOffset, _shapeGraphContainerHeight, 0);
                bar.OutlineRoot.Offset = new System.Numerics.Vector3(xOffset, _shapeGraphContainerHeight, 0);
            }

            // Scale text size.
            _textSize = _textSize * _graphHeight / oldHeight;
            // Update text render target and redraw text.
            _textRenderTarget.DotsPerInch = new Size2F((float)newDpiX, (float)newDpiY);
            _textRenderTarget.Resize(new Size2((int)(newWidth * newDpiX / 96.0), (int)(newWidth * newDpiY / 96.0)));
            DrawText(_textRenderTarget, Title, XAxisLabel, YAxisLabel, _textSize);
        }

        public void DrawText(WindowRenderTarget renderTarget, string titleText, string xAxisText, string yAxisText, float baseTextSize)
        {
            var sgOffsetY = renderTarget.Size.Height * 1 / 15;
            var sgOffsetX = renderTarget.Size.Width * 1 / 15;
            var containerHeight = renderTarget.Size.Height - sgOffsetY * 2;
            var containerWidth = renderTarget.Size.Width - sgOffsetX * 2; // not used?
            var textWidth = (int)containerHeight;
            var textHeight = (int)sgOffsetY;

            var factoryDWrite = new SharpDX.DirectWrite.Factory();

            _textFormatTitle = new TextFormat(factoryDWrite, "Segoe", baseTextSize * 5 / 4)
            {
                TextAlignment = TextAlignment.Center,
                ParagraphAlignment = ParagraphAlignment.Center
            };
            _textFormatHorizontal = new TextFormat(factoryDWrite, "Segoe", baseTextSize)
            {
                TextAlignment = TextAlignment.Center,
                ParagraphAlignment = ParagraphAlignment.Far
            };
            _textFormatVertical = new TextFormat(factoryDWrite, "Segoe", baseTextSize)
            {
                TextAlignment = TextAlignment.Center,
                ParagraphAlignment = ParagraphAlignment.Far
            };

            renderTarget.AntialiasMode = AntialiasMode.PerPrimitive;
            renderTarget.TextAntialiasMode = SharpDX.Direct2D1.TextAntialiasMode.Cleartype;

            _textSceneColorBrush = new SolidColorBrush(renderTarget, _black);

            var ClientRectangleTitle = new RectangleF(0, 0, textWidth, textHeight);
            var ClientRectangleXAxis = new RectangleF(0,
                containerHeight - textHeight + sgOffsetY * 2, textWidth, textHeight);
            var ClientRectangleYAxis = new RectangleF(-sgOffsetX,
                containerHeight - textHeight + sgOffsetY, textWidth, textHeight);

            _textSceneColorBrush.Color = _black;

            //Draw title and x axis text.
            renderTarget.BeginDraw();

            renderTarget.Clear(_white);
            renderTarget.DrawText(titleText, _textFormatTitle, ClientRectangleTitle, _textSceneColorBrush);
            renderTarget.DrawText(xAxisText, _textFormatHorizontal, ClientRectangleXAxis, _textSceneColorBrush);

            renderTarget.EndDraw();

            // Rotate render target to draw y axis text.
            renderTarget.Transform = SharpDX.Matrix3x2.Rotation((float)(-Math.PI / 2), new SharpDX.Vector2(0, containerHeight));

            renderTarget.BeginDraw();

            renderTarget.DrawText(yAxisText, _textFormatVertical, ClientRectangleYAxis, _textSceneColorBrush);

            renderTarget.EndDraw();

            // Rotate the RenderTarget back.
            renderTarget.Transform = SharpDX.Matrix3x2.Identity;
        }

        //Dispose of resources.
        public void Dispose()
        {
            _textSceneColorBrush.Dispose();
            _textFormatTitle.Dispose();
            _textFormatHorizontal.Dispose();
            _textFormatVertical.Dispose();
        }

        private Bar[] CreateBars(double[] data)
        {
            //Clear hashmap.
            _barValueMap.Clear();

            var barBrushHelper = new BarGraphUtility.BarBrushHelper(_compositor);
            var brushes = new CompositionBrush[data.Length];
            CompositionBrush brush = null;

            switch (_graphBarStyle)
            {
                case GraphBarStyle.Single:
                    brush = barBrushHelper.GenerateSingleColorBrush(_graphBarColors[0]);
                    break;
                case GraphBarStyle.Random:
                    brushes = barBrushHelper.GenerateRandomColorBrushes(data.Length);
                    break;
                case GraphBarStyle.PerBarLinearGradient:
                    brush = barBrushHelper.GenerateLinearGradient(_graphBarColors);
                    break;
                case GraphBarStyle.AmbientAnimatingPerBarLinearGradient:
                    brush = barBrushHelper.GenerateAmbientAnimatingLinearGradient(_graphBarColors);
                    break;
                default:
                    brush = barBrushHelper.GenerateSingleColorBrush(_graphBarColors[0]);
                    break;
            }

            var maxValue = _maxBarValue = GetMaxBarValue(data);
            var bars = new Bar[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                var xOffset = _shapeGraphOffsetX + _barSpacing + (_barWidth + _barSpacing) * i;
                var height = GetAdjustedBarHeight(maxValue, _graphData[i]);
                var barBrush = brush ?? brushes[i];

                var bar = new BarGraphUtility.Bar(_compositor, _shapeGraphContainerHeight, (float)height, _barWidth, "something", _graphData[i], barBrush);
                bar.OutlineRoot.Offset = new System.Numerics.Vector3(xOffset, _shapeGraphContainerHeight, 0);
                bar.Root.Offset = new System.Numerics.Vector3(xOffset, _shapeGraphContainerHeight, 0);

                _barValueMap.Add(i, bar);

                bars[i] = bar;
            }
            return bars;
        }

        private void AddBarsToTree(Bar[] bars)
        {
            BarRoot.Children.RemoveAll();
            for (int i = 0; i < bars.Length; i++)
            {
                BarRoot.Children.InsertAtTop(bars[i].OutlineRoot);
                BarRoot.Children.InsertAtTop(bars[i].Root);
            }

            AddLight();
        }

        public void UpdateGraphData(string title, string xAxisTitle, string yAxisTitle, double[] newData)
        {
            // Update properties.
            Title = title;
            XAxisLabel = xAxisTitle;
            YAxisLabel = yAxisTitle;

            // Update text.
            DrawText(_textRenderTarget, Title, XAxisLabel, YAxisLabel, _textSize);

            // Generate bars.
            // If the same number of data points, update bars with new data. Otherwise, wipe and create new.
            if (_graphData.Length == newData.Length)
            {
                var maxValue = GetMaxBarValue(newData);
                for (int i = 0; i < _graphData.Length; i++)
                {
                    // Animate bar height.
                    var oldBar = (Bar)(_barValueMap[i]);
                    var newBarHeight = GetAdjustedBarHeight(maxValue, newData[i]);

                    // Update Bar.
                    oldBar.Height = (float)newBarHeight; // Trigger height animation.
                    oldBar.Label = "something2";
                    oldBar.Value = newData[i];
                }
            }
            else
            {
                var bars = CreateBars(newData);
                AddBarsToTree(bars);
            }

            // Reset to new data.
            _graphData = newData;
        }

        private void AddLight()
        {
            _ambientLight = _compositor.CreateAmbientLight();
            _ambientLight.Color = Colors.White;
            _ambientLight.Targets.Add(_mainContainer);

            var innerConeColor = Colors.White;
            var outerConeColor = Colors.AntiqueWhite;

            _barOutlineLight = _compositor.CreateSpotLight();
            _barOutlineLight.InnerConeColor = innerConeColor;
            _barOutlineLight.OuterConeColor = outerConeColor;
            _barOutlineLight.CoordinateSpace = _mainContainer;
            _barOutlineLight.InnerConeAngleInDegrees = 45;
            _barOutlineLight.OuterConeAngleInDegrees = 80;

            _barOutlineLight.Offset = new System.Numerics.Vector3(0, 0, 80);

            // Target bars outlines with light.
            for (int i = 0; i < _barValueMap.Count; i++)
            {
                var bar = (Bar)_barValueMap[i];
                _barOutlineLight.Targets.Add(bar.OutlineRoot);
            }


            _barLight = _compositor.CreatePointLight();
            _barLight.Color = outerConeColor;
            _barLight.CoordinateSpace = _mainContainer;
            _barLight.Intensity = 0.5f;

            _barLight.Offset = new System.Numerics.Vector3(0, 0, 120);

            // Target bars with softer point light.
            for (int i = 0; i < _barValueMap.Count; i++)
            {
                var bar = _barValueMap[i];
                _barLight.Targets.Add(bar.Root);
            }
        }

        public void UpdateLight(SysWin.Point relativePoint)
        {
            _barOutlineLight.Offset = new System.Numerics.Vector3((float)relativePoint.X,
                (float)relativePoint.Y, _barOutlineLight.Offset.Z);
            _barLight.Offset = new System.Numerics.Vector3((float)relativePoint.X,
                (float)relativePoint.Y, _barLight.Offset.Z);
        }

        private double GetMaxBarValue(double[] data)
        {
            double max = data[0];
            for (int i = 0; i < data.Length; i++)
            {
                if (data[i] > max)
                {
                    max = data[i];
                }
            }
            return max;
        }

        // Adjust bar height relative to the max bar value.
        private double GetAdjustedBarHeight(double maxValue, double originalValue)
        {
            return (_shapeGraphContainerHeight - _shapeGraphOffsetY) * (originalValue / maxValue);
        }

        // Return computed bar width for graph. Default spacing is 1/2 bar width.
        private float ComputeBarWidth()
        {
            var spacingUnits = (_graphData.Length + 1) / 2;

            return ((_shapeGraphContainerWidth - (2 * _shapeGraphOffsetX)) / (_graphData.Length + spacingUnits));
        }
    }
}
