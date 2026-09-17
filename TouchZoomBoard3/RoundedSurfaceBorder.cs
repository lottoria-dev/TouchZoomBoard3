using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TouchZoomBoard
{
    // Border.CornerRadius rounds its own paint but does not clip child layers.
    // Used only for uniformly rounded popup surfaces, in device-independent units.
    internal sealed class RoundedSurfaceBorder : Border
    {
        public RoundedSurfaceBorder() { }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            UpdateClip();
        }

        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs args)
        {
            base.OnPropertyChanged(args);
            if (args.Property == CornerRadiusProperty) UpdateClip();
        }

        private void UpdateClip()
        {
            if (ActualWidth <= 0 || ActualHeight <= 0) return;
            var radius = Math.Min(CornerRadius.TopLeft, Math.Min(ActualWidth, ActualHeight) / 2);
            var clip = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight), radius, radius);
            clip.Freeze();
            Clip = clip;
        }
    }
}
