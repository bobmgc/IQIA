using System;
using System.Drawing;
using IQIAIndicator.Engine.Presentation;

namespace IQIAIndicator.Infrastructure.ATAS;

public sealed class ATASCoordinateMapper
{
    private const int DefaultX = 12;
    private const int DefaultY = 72;
    private const int DefaultWidth = 340;
    private const int DefaultHeight = 118;
    private const int VerticalSpacing = 10;

    public Rectangle Map(AnnotationAnchor anchor, int index)
    {
        int normalizedIndex = Math.Max(0, index);
        int x = anchor switch
        {
            AnnotationAnchor.Price => DefaultX + 360,
            AnnotationAnchor.Indicator => DefaultX,
            AnnotationAnchor.Viewport => DefaultX + 24,
            AnnotationAnchor.Chart => DefaultX + 48,
            _ => DefaultX
        };

        int y = anchor switch
        {
            AnnotationAnchor.Price => DefaultY,
            AnnotationAnchor.Indicator => DefaultY + 150,
            AnnotationAnchor.Viewport => DefaultY + 300,
            AnnotationAnchor.Chart => DefaultY + 450,
            _ => DefaultY
        };

        return new Rectangle(
            x,
            y + normalizedIndex * (DefaultHeight + VerticalSpacing),
            DefaultWidth,
            DefaultHeight);
    }
}
