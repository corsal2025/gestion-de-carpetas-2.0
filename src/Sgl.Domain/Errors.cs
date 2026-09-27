namespace Sgl.Domain;

/// <summary>Violation of a business rule.</summary>
public class DomainException(string message) : Exception(message);

public sealed class InvalidTransitionException(EstadoCarpeta from, EstadoCarpeta to)
    : DomainException($"Transición no permitida: {from} → {to}.")
{
    public EstadoCarpeta From { get; } = from;

    public EstadoCarpeta To { get; } = to;
}
