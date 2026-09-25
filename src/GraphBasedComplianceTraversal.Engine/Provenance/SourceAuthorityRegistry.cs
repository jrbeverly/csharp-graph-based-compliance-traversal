namespace GraphBasedComplianceTraversal.Engine.Provenance;

/// <summary>
/// The per-claim-type source-authority table. For each claim type it ranks
/// the source kinds that may back it, most authoritative first, so that when
/// two sources disagree the precedence to consult is declared rather than
/// arbitrary. A source kind absent from a claim type's ranks has no authority
/// over it, and <see cref="SourceKind.Unknown"/> may never be ranked: a fact
/// with no known source is marked, not promoted. The registry only ranks
/// sources — <see cref="MostAuthoritative"/> picks the source to consult; it
/// never resolves values. Representing a conflict and reconciling it are the
/// Ingestion milestone's work.
/// </summary>
public sealed class SourceAuthorityRegistry
{
    private readonly Dictionary<ClaimType, Dictionary<SourceKind, int>> _ranks;
    private readonly Dictionary<ClaimType, IReadOnlyList<SourceKind>> _rankedKinds;

    /// <summary>Creates an authority table from the given entries.</summary>
    public SourceAuthorityRegistry(IReadOnlyList<SourceAuthority> authorities)
    {
        ArgumentNullException.ThrowIfNull(authorities);
        ValidateAuthorities(authorities);
        Authorities = authorities;
        _ranks = authorities
            .GroupBy(authority => authority.ClaimType)
            .ToDictionary(group => group.Key, group => group.ToDictionary(authority => authority.Kind, authority => authority.Rank));
        _rankedKinds = authorities
            .GroupBy(authority => authority.ClaimType)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<SourceKind>)group
                    .OrderBy(authority => authority.Rank)
                    .Select(authority => authority.Kind)
                    .ToArray());
    }

    /// <summary>The single authoritative provenance slice for this repository.</summary>
    public static SourceAuthorityRegistry Slice { get; } = SourceAuthorityDefinition.Create();

    /// <summary>The declared authority entries, in declaration order.</summary>
    public IReadOnlyList<SourceAuthority> Authorities { get; }

    /// <summary>
    /// The rank the kind holds for the claim type, or <c>null</c> when the
    /// kind is not declared authoritative for it. Rank 1 is most
    /// authoritative.
    /// </summary>
    public int? RankFor(ClaimType claimType, SourceKind kind) =>
        _ranks.TryGetValue(claimType, out var kinds) && kinds.TryGetValue(kind, out var rank)
            ? rank
            : null;

    /// <summary>True when the kind is declared authoritative for the claim type.</summary>
    public bool IsAuthoritative(ClaimType claimType, SourceKind kind) =>
        RankFor(claimType, kind) is not null;

    /// <summary>
    /// The source kinds declared for the claim type, most authoritative
    /// first. This is the precedence to consult when sources disagree.
    /// </summary>
    public IReadOnlyList<SourceKind> RankedKinds(ClaimType claimType) =>
        _rankedKinds.TryGetValue(claimType, out var kinds) ? kinds : [];

    /// <summary>
    /// Compares two source kinds for a claim type. A ranked source always
    /// wins over an unranked one; two unranked sources are
    /// <see cref="SourcePrecedence.Incomparable"/>, and a source compared
    /// with itself is <see cref="SourcePrecedence.Equal"/>.
    /// </summary>
    public SourcePrecedence Compare(ClaimType claimType, SourceKind left, SourceKind right)
    {
        if (left == right)
        {
            return SourcePrecedence.Equal;
        }

        var leftRank = RankFor(claimType, left);
        var rightRank = RankFor(claimType, right);
        return (leftRank, rightRank) switch
        {
            (null, null) => SourcePrecedence.Incomparable,
            (not null, null) => SourcePrecedence.LeftWins,
            (null, not null) => SourcePrecedence.RightWins,
            _ => leftRank < rightRank ? SourcePrecedence.LeftWins : SourcePrecedence.RightWins,
        };
    }

    /// <summary>
    /// The record carrying the highest authority for the claim type, or
    /// <c>null</c> when no record's source is declared authoritative for it.
    /// Among records of the same authoritative source the first in insertion
    /// order is returned — the registry ranks sources, it does not reconcile
    /// values.
    /// </summary>
    public ProvenanceRecord? MostAuthoritative(ClaimType claimType, IEnumerable<ProvenanceRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        return records
            .Where(record => record.HasKnownSource && IsAuthoritative(claimType, record.Kind))
            .OrderBy(record => RankFor(claimType, record.Kind))
            .FirstOrDefault();
    }

    private static void ValidateAuthorities(IReadOnlyList<SourceAuthority> authorities)
    {
        var kindsByClaimType = new Dictionary<ClaimType, HashSet<SourceKind>>();
        var ranksByClaimType = new Dictionary<ClaimType, HashSet<int>>();

        foreach (var authority in authorities)
        {
            if (!Enum.IsDefined(authority.ClaimType))
            {
                throw new ArgumentException(
                    $"Claim type '{authority.ClaimType}' is not a declared ClaimType member.",
                    nameof(authorities));
            }

            if (!Enum.IsDefined(authority.Kind))
            {
                throw new ArgumentException(
                    $"Source kind '{authority.Kind}' is not a declared SourceKind member.",
                    nameof(authorities));
            }

            if (authority.Kind == SourceKind.Unknown)
            {
                throw new ArgumentException(
                    "SourceKind.Unknown may not be ranked: a fact with no known source is marked, never made authoritative.",
                    nameof(authorities));
            }

            if (authority.Rank <= 0)
            {
                throw new ArgumentException(
                    $"Authority ranks are 1-based (rank 1 is most authoritative); got {authority.Rank}.",
                    nameof(authorities));
            }

            ArgumentException.ThrowIfNullOrWhiteSpace(authority.Rationale);

            if (!kindsByClaimType.TryGetValue(authority.ClaimType, out var kinds))
            {
                kindsByClaimType[authority.ClaimType] = kinds = [];
            }

            if (!kinds.Add(authority.Kind))
            {
                throw new ArgumentException(
                    $"Source kind '{authority.Kind}' is ranked more than once for claim type '{authority.ClaimType}'.",
                    nameof(authorities));
            }

            if (!ranksByClaimType.TryGetValue(authority.ClaimType, out var ranks))
            {
                ranksByClaimType[authority.ClaimType] = ranks = [];
            }

            if (!ranks.Add(authority.Rank))
            {
                throw new ArgumentException(
                    $"Rank {authority.Rank} is used more than once for claim type '{authority.ClaimType}': " +
                    "precedence must be a total order, so ranks within a claim type must be distinct.",
                    nameof(authorities));
            }
        }
    }
}
