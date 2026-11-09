using System.Text.Json.Serialization;

namespace Ietp.Telemetry.Contracts;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(NumericTelemetryValue), "number")]
[JsonDerivedType(typeof(BooleanTelemetryValue), "boolean")]
[JsonDerivedType(typeof(TextTelemetryValue), "text")]
public abstract record TelemetryValue;

public sealed record NumericTelemetryValue(double Value) : TelemetryValue;
public sealed record BooleanTelemetryValue(bool Value) : TelemetryValue;
public sealed record TextTelemetryValue(string Value) : TelemetryValue;
