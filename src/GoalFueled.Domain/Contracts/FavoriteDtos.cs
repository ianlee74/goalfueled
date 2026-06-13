namespace GoalFueled.Domain.Contracts;

/// <summary>Payload for setting whether an ingredient/recipe/meal is one of the caller's favorites.</summary>
public sealed record FavoriteInput(bool IsFavorite);

/// <summary>The resulting favorite state for one item, as returned to clients.</summary>
public sealed record FavoriteStateDto(string Id, bool IsFavorite);
