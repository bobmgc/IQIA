using System;
using System.Collections.Generic;
using IQIAIndicator.Engine.Presentation;
using OFT.Rendering.Context;

namespace IQIAIndicator.Infrastructure.ATAS;

public sealed class ATASRenderer
{
    private readonly ATASAnnotationMapper _annotationMapper;
    private readonly ATASCoordinateMapper _coordinateMapper;
    private readonly ATASDrawingFactory _drawingFactory;

    public ATASRenderer()
        : this(new ATASAnnotationMapper(), new ATASCoordinateMapper(), new ATASDrawingFactory())
    {
    }

    public ATASRenderer(
        ATASAnnotationMapper annotationMapper,
        ATASCoordinateMapper coordinateMapper,
        ATASDrawingFactory drawingFactory)
    {
        _annotationMapper = annotationMapper ?? throw new ArgumentNullException(nameof(annotationMapper));
        _coordinateMapper = coordinateMapper ?? throw new ArgumentNullException(nameof(coordinateMapper));
        _drawingFactory = drawingFactory ?? throw new ArgumentNullException(nameof(drawingFactory));
    }

    public void Render(RenderContext renderContext, ChartAnnotationCandidate candidate)
    {
        if (renderContext is null)
        {
            throw new ArgumentNullException(nameof(renderContext));
        }

        if (candidate is null)
        {
            throw new ArgumentNullException(nameof(candidate));
        }

        if (candidate.Annotations is null || candidate.Annotations.Count == 0)
        {
            return;
        }

        for (int index = 0; index < candidate.Annotations.Count; index++)
        {
            ChartAnnotation annotation = candidate.Annotations[index];
            var bounds = _coordinateMapper.Map(annotation.Anchor, index);
            ATASAnnotationDescriptor descriptor = _annotationMapper.Map(annotation, bounds);
            ATASDrawing drawing = _drawingFactory.Create(descriptor);

            drawing.RenderTo(renderContext);
        }
    }

    public IReadOnlyDictionary<string, string> Describe(ChartAnnotationCandidate candidate)
    {
        if (candidate is null)
            throw new ArgumentNullException(nameof(candidate));

        if (candidate.Annotations is null || candidate.Annotations.Count == 0)
            return new Dictionary<string, string>
            {
                ["Position"] = "None",
                ["Color"] = "None",
                ["Text"] = string.Empty
            };

        ChartAnnotation annotation = candidate.Annotations[0];
        var bounds = _coordinateMapper.Map(annotation.Anchor, 0);
        ATASAnnotationDescriptor descriptor = _annotationMapper.Map(annotation, bounds);
        return new Dictionary<string, string>
        {
            ["Position"] = bounds.ToString(),
            ["Color"] = $"{descriptor.Style.Category}/{descriptor.Style.Theme}/{descriptor.Style.Severity}",
            ["Text"] = string.Join(" | ", descriptor.Lines)
        };
    }
}
