﻿using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using SDRSharp.Radio;

namespace SDRSharp.PanView
{
    public enum BandType
    {
        Lower,
        Upper,
        Center
    }

    public delegate void ManualFrequencyChange(object sender, FrequencyEventArgs e);
    public delegate void ManualBandwidthChange(object sender, BandwidthEventArgs e);

    public class Waterfall : UserControl
    {
        private const int CarrierPenWidth = 1;
        private const int AxisMargin = 30;

        public const int CursorSnapDistance = 4;
        public const float MaxZoom = 4.0f;
        public const int RightClickSnapDistance = 500; // Snap distance in Hz, for Ellie

        private double _attack;
        private double _decay;
        private bool _performNeeded;
        private Bitmap _buffer;
        private Bitmap _buffer2;
        private Graphics _graphics;
        private Graphics _graphics2;
        private BandType _bandType;
        private int _filterBandwidth;
        private int _filterOffset;
        private float _xIncrement;
        private byte[] _temp;
        private byte[] _spectrum;
        private long _centerFrequency;
        private long _spectrumWidth;
        private int _stepSize;
        private long _frequency;
        private float _lower;
        private float _upper;
        private float _scale = 1f;
        private long _displayCenterFrequency;
        private bool _changingBandwidth;
        private bool _changingFrequency;
        private bool _changingCenterFrequency;
        private bool _mouseIn;
        private int _oldX;
        private long _oldFrequency;
        private long _oldCenterFrequency;
        private int _oldFilterBandwidth;
        private int[] _gradientPixels;
        private int _contrast;
        private int _zoom;
        private bool _useSmoothing;
        private bool _useSnap;
        private float _trackingY;
        private float _trackingX;
        private long _trackingFrequency;
        private LinearGradientBrush _gradientBrush;
        private ColorBlend _gradientColorBlend = GetGradientBlend();

        public Waterfall()
        {
            _spectrum = new byte[ClientRectangle.Width - 2 * AxisMargin];
            _temp = new byte[_spectrum.Length];
            _buffer = new Bitmap(ClientRectangle.Width, ClientRectangle.Height, PixelFormat.Format32bppPArgb);
            _buffer2 = new Bitmap(ClientRectangle.Width, ClientRectangle.Height, PixelFormat.Format32bppPArgb);
            _graphics = Graphics.FromImage(_buffer);
            _graphics2 = Graphics.FromImage(_buffer2);
            _gradientBrush = new LinearGradientBrush(new Rectangle(AxisMargin / 2, AxisMargin / 2, ClientRectangle.Width - AxisMargin / 2, ClientRectangle.Height - AxisMargin / 2), Color.White, Color.Black, LinearGradientMode.Vertical);
            _gradientBrush.InterpolationColors = _gradientColorBlend;

            SetStyle(ControlStyles.OptimizedDoubleBuffer, false);
            SetStyle(ControlStyles.DoubleBuffer, false);
            SetStyle(ControlStyles.UserPaint, true);
            SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            UpdateStyles();
        }

        public static ColorBlend GetGradientBlend()
        {
            return Utils.GetGradientBlend(255);
        }

        ~Waterfall()
        {
            _buffer.Dispose();
            _buffer2.Dispose();
            _graphics.Dispose();
            _graphics2.Dispose();
            _gradientBrush.Dispose();
        }

        public void Perform()
        {
            if (_performNeeded)
            {
                _performNeeded = false;
                Invalidate();
            }
        }

        public event ManualFrequencyChange FrequencyChanged;

        public event ManualFrequencyChange CenterFrequencyChanged;

        public event ManualBandwidthChange BandwidthChanged;

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public ColorBlend GradientColorBlend
        {
            get
            {
                return _gradientColorBlend;
            }
            set
            {
                if (_gradientColorBlend != value)
                {
                    _gradientColorBlend = value;

                    _gradientBrush.Dispose();
                    _gradientBrush = new LinearGradientBrush(new Rectangle(AxisMargin / 2, AxisMargin / 2, ClientRectangle.Width - AxisMargin / 2, ClientRectangle.Height - AxisMargin / 2), Color.White, Color.Black, LinearGradientMode.Vertical);
                    _gradientPixels = null;
                    _gradientBrush.InterpolationColors = _gradientColorBlend;
                    
                    DrawGradient();
                    BuildGradientVector();

                    _performNeeded = true;
                }
            }
        }

        public long CenterFrequency
        {
            get
            {
                return _centerFrequency;
            }
            set
            {
                if (_centerFrequency != value)
                {
                    var delta = value - _centerFrequency;
                    _displayCenterFrequency += delta;
                    _centerFrequency = value;
                    _performNeeded = true;
                }
            }
        }

        public int SpectrumWidth
        {
            get
            {
                return (int) _spectrumWidth;
            }
            set
            {
                if (_spectrumWidth != value)
                {
                    _spectrumWidth = value;
                    ApplyZoom();
                }
            }
        }

        public long Frequency
        {
            get
            {
                return _frequency;
            }
            set
            {
                if (_frequency != value)
                {
                    _frequency = value;
                    _performNeeded = true;
                }
            }
        }

        public int FilterBandwidth
        {
            get
            {
                return _filterBandwidth;
            }
            set
            {
                if (_filterBandwidth != value)
                {
                    _filterBandwidth = value;
                    _performNeeded = true;
                }
            }
        }

        public int FilterOffset
        {
            get
            {
                return _filterOffset;
            }
            set
            {
                if (_filterOffset != value)
                {
                    _filterOffset = value;
                    _performNeeded = true;
                }
            }
        }

        public BandType BandType
        {
            get
            {
                return _bandType;
            }
            set
            {
                if (_bandType != value)
                {
                    _bandType = value;
                    _performNeeded = true;
                }
            }
        }

        public int Contrast
        {
            get
            {
                return _contrast;
            }
            set
            {
                _contrast = value;
            }
        }

        public int Zoom
        {
            get
            {
                return _zoom;
            }
            set
            {
                if (_zoom != value)
                {
                    _zoom = value;
                    ApplyZoom();
                }
            }
        }

        public bool UseSmoothing
        {
            get { return _useSmoothing; }
            set { _useSmoothing = value; }
        }

        public double Decay
        {
            get { return _decay; }
            set { _decay = value; }
        }

        public double Attack
        {
            get { return _attack; }
            set { _attack = value; }
        }

        public int StepSize
        {
            get { return _stepSize; }
            set
            {
                _performNeeded = true;
                _stepSize = value;
            }
        }

        public bool UseSnap
        {
            get { return _useSnap; }
            set { _useSnap = value; }
        }

        private void ApplyZoom()
        {
            _scale = (float) Math.Pow(10, _zoom * MaxZoom / 100.0f);
            _displayCenterFrequency = GetDisplayCenterFrequency();
            if (_spectrumWidth > 0)
            {
                _xIncrement = _scale * (ClientRectangle.Width - 2 * AxisMargin) / _spectrumWidth;
                _performNeeded = true;
            }
        }

        private long GetDisplayCenterFrequency()
        {
            var f = _frequency;
            switch (_bandType)
            {
                case BandType.Lower:
                    f -= _filterBandwidth / 2 + _filterOffset;
                    break;

                case BandType.Upper:
                    f += _filterBandwidth / 2 + _filterOffset;
                    break;
            }
            var lowerLeadingSpectrum = (long) ((_centerFrequency - _spectrumWidth / 2) - (f - _spectrumWidth / _scale / 2));
            if (lowerLeadingSpectrum > 0)
            {
                f += lowerLeadingSpectrum + 10;
            }

            var upperLeadingSpectrum = (long) ((f + _spectrumWidth / _scale / 2) - (_centerFrequency + _spectrumWidth / 2));
            if (upperLeadingSpectrum > 0)
            {
                f -= upperLeadingSpectrum + 10;
            }

            return f;
        }

        public unsafe static void SmoothCopy(byte[] source, byte[] destination, int sourceLength, float scale, int offset)
        {
            fixed (byte* srcPtr = source)
            fixed (byte* dstPtr = destination)
            {
                var r = sourceLength / scale / destination.Length;
                if (r > 1.0f)
                {
                    var n = (int)Math.Ceiling(r);
                    for (var i = 0; i < destination.Length; i++)
                    {
                        var k = (int)(i * r - n / 2.0f);
                        var max = (byte)0;
                        for (var j = 0; j < n; j++)
                        {
                            var index = k + j + offset;
                            if (index >= 0 && index < sourceLength)
                            {
                                if (max < srcPtr[index])
                                {
                                    max = srcPtr[index];
                                }
                            }
                        }
                        dstPtr[i] = max;
                    }
                }
                else
                {
                    for (var i = 0; i < destination.Length; i++)
                    {
                        var index = (int)(r * i + offset);
                        if (index >= 0 && index < sourceLength)
                        {
                            dstPtr[i] = srcPtr[index];
                        }
                    }
                }
            }
        }

        public void Render(byte[] spectrum, int length)
        {
            var scaledLength = (int)(length / _scale);
            var offset = (int)((length - scaledLength) / 2.0 + length * (double)(_displayCenterFrequency - _centerFrequency) / _spectrumWidth);

            if (_useSmoothing)
            {
                SmoothCopy(spectrum, _temp, length, _scale, offset);
                for (var i = 0; i < _spectrum.Length; i++)
                {
                    var ratio = _spectrum[i] < _temp[i] ? Attack : Decay;
                    _spectrum[i] = (byte) (_spectrum[i] * (1 - ratio) + _temp[i] * ratio);
                }
            }
            else
            {
                SmoothCopy(spectrum, _spectrum, length, _scale, offset);
            }
            Draw();
            _performNeeded = true;
        }

        private unsafe void Draw()
        {
            #region Draw only if needed

            if (ClientRectangle.Width <= AxisMargin || ClientRectangle.Height <= AxisMargin)
            {
                return;
            }

            #endregion

            #region Shift image

            var bmpData = _buffer.LockBits(ClientRectangle, ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
            var ptr = (void*) ((long)bmpData.Scan0 + bmpData.Width * 4);
            Utils.Memcpy(ptr, (void*) bmpData.Scan0, (bmpData.Height - 1) * bmpData.Width * 4);
            _buffer.UnlockBits(bmpData);

            DrawGradient();

            #endregion

            #region Draw Spectrum

            DrawSpectrum();

            #endregion
        }

        private unsafe void DrawSpectrum()
        {
            if (_spectrum == null || _spectrum.Length == 0)
            {
                return;
            }
            var bits = _buffer.LockBits(ClientRectangle, ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
            var ptr = (int*) bits.Scan0 + AxisMargin;
            for (var i = 0; i < _spectrum.Length; i++)
            {
                var colorIndex = (int)((_spectrum[i] + _contrast * 50.0 / 25.0) * _gradientPixels.Length / byte.MaxValue);
                colorIndex = Math.Max(colorIndex, 0);
                colorIndex = Math.Min(colorIndex, _gradientPixels.Length - 1);
                
                *ptr++ = _gradientPixels[colorIndex];
            }
            _buffer.UnlockBits(bits);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (_mouseIn)
            {
                _graphics2.DrawImageUnscaled(_buffer, 0, 0);
                DrawCursor(_graphics2);
                e.Graphics.DrawImageUnscaled(_buffer2, 0, 0);
            }
            else
            {
                e.Graphics.DrawImageUnscaled(_buffer, 0, 0);
            }
        }

        private void DrawCursor(Graphics g)
        {
            _lower = 0f;
            float bandpassOffset;
            var bandpassWidth = 0f;
            var cursorWidth = Math.Max((_filterBandwidth + _filterOffset) * _xIncrement, 2);
            var xCarrier = (float)ClientRectangle.Width / 2 + (_frequency - _displayCenterFrequency) * _xIncrement;

            switch (_bandType)
            {
                case BandType.Upper:
                    bandpassOffset = _filterOffset * _xIncrement;
                    bandpassWidth = cursorWidth - bandpassOffset;
                    _lower = xCarrier + bandpassOffset;
                    break;

                case BandType.Lower:
                    bandpassOffset = _filterOffset * _xIncrement;
                    bandpassWidth = cursorWidth - bandpassOffset;
                    _lower = xCarrier - bandpassOffset - bandpassWidth;
                    break;

                case BandType.Center:
                    _lower = xCarrier - cursorWidth / 2;
                    bandpassWidth = cursorWidth;
                    break;
            }
            _upper = _lower + bandpassWidth;

            using (var transparentBrush = new SolidBrush(Color.FromArgb(80, Color.DarkGray)))
            using (var hotTrackPen = new Pen(Color.Red))
            using (var carrierPen = new Pen(Color.Red))
            using (var font = new Font("Arial", 12f))
            {
                carrierPen.Width = CarrierPenWidth;
                if (cursorWidth < ClientRectangle.Width)
                {
                    g.SmoothingMode = SmoothingMode.HighSpeed;
                    g.FillRectangle(transparentBrush, _lower, 0, bandpassWidth, ClientRectangle.Height);
                    if (xCarrier >= AxisMargin && xCarrier <= ClientRectangle.Width - AxisMargin)
                    {
                        g.DrawLine(carrierPen, xCarrier, 0f, xCarrier, ClientRectangle.Height);
                    }
                }
                if (_trackingX >= AxisMargin && _trackingX <= ClientRectangle.Width - AxisMargin)
                {
                    if (!_changingFrequency && !_changingCenterFrequency && !_changingBandwidth)
                    {
                        _graphics2.DrawLine(hotTrackPen, _trackingX, 0f, _trackingX, ClientRectangle.Height);
                    }

                    var fstring = SpectrumAnalyzer.GetFrequencyDisplay(_changingFrequency ? _frequency : _changingBandwidth ? _filterBandwidth : _trackingFrequency);
                    var stringSize = g.MeasureString(fstring, font);
                    var xOffset = _trackingX + 15.0f;
                    var yOffset = _trackingY + (Cursor.Current == null ? SpectrumAnalyzer.DefaultCursorHeight : Cursor.Current.Size.Height) - 8.0f;
                    xOffset = Math.Min(xOffset, ClientRectangle.Width - stringSize.Width + 4);
                    yOffset = Math.Min(yOffset, ClientRectangle.Height - stringSize.Height + 1);
                    g.DrawString(fstring, font, Brushes.White, (int) xOffset, (int) yOffset, StringFormat.GenericTypographic);
                }
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (ClientRectangle.Width <= AxisMargin || ClientRectangle.Height <= AxisMargin)
            {
                return;
            }
            var temp = new byte[ClientRectangle.Width - 2 * AxisMargin];
            SmoothCopy(_spectrum, temp, _spectrum.Length, (_temp.Length + temp.Length) / (float) _temp.Length, 0);
            _spectrum = temp;
            _temp = new byte[_spectrum.Length];
            
            var oldBuffer = _buffer;
            _buffer = new Bitmap(ClientRectangle.Width, ClientRectangle.Height, PixelFormat.Format32bppPArgb);
            var oldBuffer2 = _buffer2;
            _buffer2 = new Bitmap(ClientRectangle.Width, ClientRectangle.Height, PixelFormat.Format32bppPArgb);
            _graphics.Dispose();
            _graphics = Graphics.FromImage(_buffer);
            _graphics.SmoothingMode = SmoothingMode.HighSpeed;
            _graphics2.Dispose();
            _graphics2 = Graphics.FromImage(_buffer2);
            _graphics2.SmoothingMode = SmoothingMode.HighSpeed;
            using (var bkgBrush = new SolidBrush(Color.Black))
            {
                _graphics.FillRectangle(bkgBrush, ClientRectangle);
            }
            var rect = new Rectangle(AxisMargin, 0, _buffer.Width - 2 * AxisMargin, _buffer.Height);
            _graphics.DrawImage(oldBuffer, rect, AxisMargin, 0, oldBuffer.Width - 2 * AxisMargin, oldBuffer.Height, GraphicsUnit.Pixel);
            oldBuffer.Dispose();
            oldBuffer2.Dispose();
            if (_spectrumWidth > 0)
            {
                _xIncrement = _scale * (ClientRectangle.Width - 2 * AxisMargin) / _spectrumWidth;
            }
            _gradientBrush.Dispose();
            _gradientBrush = new LinearGradientBrush(new Rectangle(AxisMargin / 2, AxisMargin / 2, Width - AxisMargin / 2, ClientRectangle.Height - AxisMargin / 2), Color.White, Color.Black, LinearGradientMode.Vertical);
            _gradientPixels = null;
            _gradientBrush.InterpolationColors = _gradientColorBlend;
            DrawGradient();
            BuildGradientVector();
            Invalidate();
        }

        private void DrawGradient()
        {
            using (var pen = new Pen(_gradientBrush, 10))
            {
                _graphics.FillRectangle(Brushes.Black,
                                   ClientRectangle.Width - AxisMargin,
                                   0,
                                   AxisMargin,
                                   ClientRectangle.Height);
                _graphics.DrawLine(pen,
                                   ClientRectangle.Width - AxisMargin / 2,
                                   ClientRectangle.Height - AxisMargin / 2,
                                   ClientRectangle.Width - AxisMargin / 2,
                                   AxisMargin / 2);
            }
        }

        private void BuildGradientVector()
        {
            if (_gradientPixels == null || _gradientPixels.Length != ClientRectangle.Height - AxisMargin)
            {
                _gradientPixels = new int[ClientRectangle.Height - AxisMargin - 1];
            }
            for (var i = 0; i < _gradientPixels.Length; i++)
            {
                _gradientPixels[_gradientPixels.Length - i - 1] = _buffer.GetPixel(ClientRectangle.Width - AxisMargin / 2, i + AxisMargin / 2 + 1).ToArgb();
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // Prevent default background painting
        }

        protected virtual void OnFrequencyChanged(FrequencyEventArgs e)
        {
            if (FrequencyChanged != null)
            {
                FrequencyChanged(this, e);
            }
        }

        protected virtual void OnCenterFrequencyChanged(FrequencyEventArgs e)
        {
            if (CenterFrequencyChanged != null)
            {
                CenterFrequencyChanged(this, e);
            }
        }

        protected virtual void OnBandwidthChanged(BandwidthEventArgs e)
        {
            if (BandwidthChanged != null)
            {
                BandwidthChanged(this, e);
            }
        }

        private void UpdateFrequency(long f)
        {
            var min = (long)(_displayCenterFrequency - _spectrumWidth / _scale / 2);
            if (f < min)
            {
                f = min;
            }
            var max = (long)(_displayCenterFrequency + _spectrumWidth / _scale / 2);
            if (f > max)
            {
                f = max;
            }

            if (_useSnap)
            {
                f = (f + Math.Sign(f) * _stepSize / 2) / _stepSize * _stepSize;
            }

            if (f != _frequency)
            {
                OnFrequencyChanged(new FrequencyEventArgs(f));
            }
        }

        private void UpdateCenterFrequency(long f)
        {
            if (f < 0)
            {
                f = 0;
            }

            if (_useSnap)
            {
                f = (f + Math.Sign(f) * _stepSize / 2) / _stepSize * _stepSize;
            }

            if (f != _centerFrequency)
            {
                OnCenterFrequencyChanged(new FrequencyEventArgs(f));
            }
        }

        private void UpdateBandwidth(int bw)
        {
            bw = 10 * (bw / 10);
            
            if (bw < 10)
            {
                bw = 10;
            }

            if (bw != _filterBandwidth)
            {
                OnBandwidthChanged(new BandwidthEventArgs(bw));
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (e.Button == MouseButtons.Left)
            {
                var cursorWidth = Math.Max(_filterBandwidth * _xIncrement, 2);
                if (e.X > _lower && e.X < _upper && cursorWidth < ClientRectangle.Width)
                {
                    _oldX = e.X;
                    _oldFrequency = _frequency;
                    _changingFrequency = true;
                }
                else if ((Math.Abs(e.X - _lower + CursorSnapDistance) <= CursorSnapDistance &&
                    (_bandType == BandType.Center || _bandType == BandType.Lower))
                    ||
                    (Math.Abs(e.X - _upper - CursorSnapDistance) <= CursorSnapDistance &&
                    (_bandType == BandType.Center || _bandType == BandType.Upper)))
                {
                    _oldX = e.X;
                    _oldFilterBandwidth = _filterBandwidth;
                    _changingBandwidth = true;
                }
                else
                {
                    _oldX = e.X;
                    _oldCenterFrequency = _centerFrequency;
                    _changingCenterFrequency = true;
                }
            }
            else if (e.Button == MouseButtons.Right)
            {
                UpdateFrequency(_frequency / RightClickSnapDistance * RightClickSnapDistance);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_changingCenterFrequency && e.X == _oldX)
            {
                var f = (long)((_oldX - ClientRectangle.Width / 2) * _spectrumWidth / _scale / (ClientRectangle.Width - 2 * AxisMargin) + _displayCenterFrequency);
                UpdateFrequency(f);
            }
            _changingCenterFrequency = false;
            _performNeeded = true;
            _changingBandwidth = false;
            _changingFrequency = false;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            _trackingX = e.X;
            _trackingY = e.Y;
            _trackingFrequency = (long)((e.X - ClientRectangle.Width / 2) * _spectrumWidth / _scale / (ClientRectangle.Width - 2 * AxisMargin) + _displayCenterFrequency);
            if (_useSnap)
            {
                _trackingFrequency = (_trackingFrequency + Math.Sign(_trackingFrequency) * _stepSize / 2) / _stepSize * _stepSize;
            }

            if (_changingFrequency)
            {
                var f = (long) ((e.X - _oldX) * _spectrumWidth / _scale / (ClientRectangle.Width - 2 * AxisMargin) + _oldFrequency);
                UpdateFrequency(f);
            }
            else if (_changingCenterFrequency)
            {
                var f = (long) ((_oldX - e.X) * _spectrumWidth / _scale / (ClientRectangle.Width - 2 * AxisMargin) + _oldCenterFrequency);
                UpdateCenterFrequency(f);
            }
            else if (_changingBandwidth)
            {
                var bw = 0;
                switch (_bandType)
                {
                    case BandType.Upper:
                        bw = e.X - _oldX;
                        break;

                    case BandType.Lower:
                        bw = _oldX - e.X;
                        break;

                    case BandType.Center:
                        bw = (_oldX > (_lower + _upper) / 2 ? e.X - _oldX : _oldX - e.X) * 2;
                        break;
                }
                bw = (int) (bw * _spectrumWidth / _scale / (ClientRectangle.Width - 2 * AxisMargin) + _oldFilterBandwidth);
                UpdateBandwidth(bw);
            }
            else if ((Math.Abs(e.X - _lower + CursorSnapDistance) <= CursorSnapDistance &&
                (_bandType == BandType.Center || _bandType == BandType.Lower))
                ||
                (Math.Abs(e.X - _upper - CursorSnapDistance) <= CursorSnapDistance &&
                (_bandType == BandType.Center || _bandType == BandType.Upper)))
            {
                Cursor = Cursors.SizeWE;
            }
            else
            {
                Cursor = Cursors.Default;
            }
            _performNeeded = true;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _mouseIn = true;
            _performNeeded = true;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _mouseIn = false;
            _performNeeded = true;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            UpdateFrequency(_frequency + (_useSnap ? _stepSize * Math.Sign(e.Delta) : e.Delta / 10));
        }
    }

    public class FrequencyEventArgs : EventArgs
    {
        public long Frequency { get; set; }

        public FrequencyEventArgs(long frequency)
        {
            Frequency = frequency;
        }
    }

    public class BandwidthEventArgs : EventArgs
    {
        public int Bandwidth { get; set; }

        public BandwidthEventArgs(int bandwidth)
        {
            Bandwidth = bandwidth;
        }
    }
}