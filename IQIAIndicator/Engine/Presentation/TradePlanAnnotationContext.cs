namespace IQIAIndicator.Engine.Presentation;

// Sprint 15.24 (Lot 1 - chart display): TradePlan is fully qualified with global:: because this file's
// namespace (IQIAIndicator.Engine.Presentation) is a sibling of IQIAIndicator.Engine.TradePlan under
// the common parent IQIAIndicator.Engine - an unqualified "TradePlan" resolves to that sibling
// namespace instead of the record type (CS0118), the same class-of-namespaces situation
// IQIAIndicator.cs already works around for its own ScientificModels.Abstractions.MarketContext field.
public sealed record TradePlanAnnotationContext(global::IQIAIndicator.Engine.TradePlan.TradePlan? TradePlan);
