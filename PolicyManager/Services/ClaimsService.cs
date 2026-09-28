using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using PolicyManager.Data;
using PolicyManager.DTOs;
using PolicyManager.Domain;
using PolicyManager.Exceptions;
using PolicyManager.Models;
using PolicyManager.Models.Enums;

namespace PolicyManager.Services;

/// <summary>
///     Service implementation for managing insurance claims with transactional outbox support.
/// </summary>
public class ClaimsService(AppDbContext context, IBusinessNumberGenerator numbers) : IClaimsService
{
    /// <summary>
    ///     Retrieves one page of claims, ordered, with the total count of the whole result set.
    /// </summary>
    /// <param name="pagination">The requested page, page size and sort. Normalized before use.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A page of claims as ClaimDto objects.</returns>
    public async Task<PagedResult<ClaimDto>> GetAll(
        PaginationQuery pagination, CancellationToken cancellationToken = default)
    {
        pagination.Normalize();

        var totalCount = await context.Claims.CountAsync(cancellationToken);

        // The sort and the offset are applied to the entity query and the projection follows them, so
        // the database does the paging and only the requested rows come back.
        var items = await ApplySorting(context.Claims, pagination.SortBy, pagination.Descending)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .Select(c => new ClaimDto
            {
                Id = c.Id,
                ClaimNumber = c.ClaimNumber,
                PolicyId = c.PolicyId,
                Amount = c.Amount,
                Description = c.Description,
                Status = c.Status,
                FiledAt = c.FiledAt,
                DecisionDate = c.DecisionDate,
                DecidedBy = c.DecidedBy,
                AdjusterNotes = c.AdjusterNotes,
                UpdatedAt = c.UpdatedAt,
                UpdatedBy = c.UpdatedBy,
                RowVersion = ConcurrencyTokens.ToToken(c.RowVersion)
            })
            .ToListAsync(cancellationToken);

        return PagedResult<ClaimDto>.Create(items, totalCount, pagination.Page, pagination.PageSize);
    }

    /// <summary>
    ///     Orders the claim query by the requested field.
    /// </summary>
    /// <remarks>
    ///     An absent or unrecognised key falls back to the identifier, which is unique and therefore
    ///     the only ordering that is total. Every branch appends the identifier as a tie-breaker, so
    ///     rows with the same amount or status keep a defined order and a page number always
    ///     identifies the same rows.
    /// </remarks>
    /// <param name="query">The claims to order.</param>
    /// <param name="sortBy">The requested field name, or null for the default.</param>
    /// <param name="descending">Whether to reverse the order.</param>
    /// <returns>The ordered query.</returns>
    private static IQueryable<Claim> ApplySorting(IQueryable<Claim> query, string? sortBy, bool descending)
    {
        return (sortBy?.Trim().ToLowerInvariant()) switch
        {
            "claimnumber" => Order(query, descending, c => c.ClaimNumber),
            "amount" => Order(query, descending, c => c.Amount),
            "status" => Order(query, descending, c => c.Status),
            "filedat" => Order(query, descending, c => c.FiledAt),
            "policyid" => Order(query, descending, c => c.PolicyId),
            _ => Order(query, descending, c => c.Id)
        };
    }

    /// <summary>
    ///     Applies an ascending or descending order on one field, breaking ties on the identifier.
    /// </summary>
    /// <param name="query">The claims to order.</param>
    /// <param name="descending">Whether to reverse the order.</param>
    /// <param name="keySelector">The field to order by.</param>
    /// <typeparam name="TKey">The type of the ordering field.</typeparam>
    /// <returns>The ordered query.</returns>
    private static IOrderedQueryable<Claim> Order<TKey>(
        IQueryable<Claim> query, bool descending, Expression<Func<Claim, TKey>> keySelector)
    {
        return descending
            ? query.OrderByDescending(keySelector).ThenBy(c => c.Id)
            : query.OrderBy(keySelector).ThenBy(c => c.Id);
    }

    /// <summary>
    ///     Retrieves a claim by its unique identifier.
    /// </summary>
    /// <param name="id">The claim identifier.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The claim if found; otherwise, null.</returns>
    public async Task<ClaimDto?> GetById(int id, CancellationToken cancellationToken = default)
    {
        return await context.Claims
            .Where(c => c.Id == id)
            .Select(c => new ClaimDto
            {
                Id = c.Id,
                ClaimNumber = c.ClaimNumber,
                PolicyId = c.PolicyId,
                Amount = c.Amount,
                Description = c.Description,
                Status = c.Status,
                FiledAt = c.FiledAt,
                DecisionDate = c.DecisionDate,
                DecidedBy = c.DecidedBy,
                AdjusterNotes = c.AdjusterNotes,
                UpdatedAt = c.UpdatedAt,
                UpdatedBy = c.UpdatedBy,
                RowVersion = ConcurrencyTokens.ToToken(c.RowVersion)
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    ///     Creates a new claim and records an outbox message transactionally.
    /// </summary>
    /// <remarks>
    ///     The policy is loaded rather than merely checked for existence, because the rules that
    ///     govern a filing need its status and coverage limit. Previously only existence was
    ///     verified, so a claim could be filed against a cancelled policy and paid out.
    /// </remarks>
    /// <param name="dto">The claim data transfer object.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The unique identifier of the newly created claim.</returns>
    /// <exception cref="NotFoundException">The referenced policy does not exist.</exception>
    /// <exception cref="BusinessRuleException">The policy will not accept this claim.</exception>
    public async Task<int> Create(CreateClaimDto dto, CancellationToken cancellationToken = default)
    {
        var policy = await context.Policies
            .FirstOrDefaultAsync(p => p.Id == dto.PolicyId, cancellationToken)
            ?? throw new NotFoundException("Policy", dto.PolicyId);

        ClaimRules.EnsurePolicyAcceptsClaims(policy);

        var alreadyClaimed = await context.Claims
            .Where(c => c.PolicyId == policy.Id)
            .Where(IsRecognisedAgainstCoverage)
            .SumAsync(c => (decimal?)c.Amount, cancellationToken) ?? 0m;

        ClaimRules.EnsureWithinCoverage(policy, dto.Amount, alreadyClaimed);

        var claim = new Claim
        {
            ClaimNumber = await numbers.NextAsync(BusinessNumberGenerator.ClaimKind, cancellationToken),
            PolicyId = dto.PolicyId,
            Amount = dto.Amount,
            Description = dto.Description,
            Status = ClaimStatus.Pending,
            FiledAt = DateTime.UtcNow
        };

        await context.AddWithOutboxAsync(
            claim,
            "ClaimCreated",
            c => new { c.Id, c.PolicyId, c.Amount, c.Status, c.FiledAt },
            cancellationToken);

        return claim.Id;
    }

    /// <summary>
    ///     Updates the status of an existing claim and records an outbox message.
    /// </summary>
    /// <param name="id">The claim identifier.</param>
    /// <param name="dto">The claim status update data transfer object containing the new status.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    public async Task UpdateStatus(
        int id,
        UpdateClaimStatusDto dto,
        string? decidedBy = null,
        string? adjusterNotes = null,
        string? rowVersion = null,
        CancellationToken cancellationToken = default)
    {
        var target = dto.Status ?? throw new InvalidOperationException(
            $"{nameof(UpdateClaimStatusDto.Status)} is required and was not supplied.");

        var claim = await context.Claims.FindAsync([id], cancellationToken)
            ?? throw new NotFoundException("Claim", id);

        if (!ClaimStatusTransitions.IsAllowed(claim.Status, target))
        {
            throw new BusinessRuleException(
                $"A claim cannot move from {claim.Status} to {target}. " +
                $"Permitted from {claim.Status}: {ClaimStatusTransitions.DescribeAllowed(claim.Status)}.",
                IllegalTransitionRule);
        }

        claim.Status = target;
        claim.DecisionDate = DateTime.UtcNow;
        claim.DecidedBy = decidedBy;
        claim.AdjusterNotes = adjusterNotes;

        context.ApplyOriginalValue(claim, rowVersion);

        await context.SaveWithOutboxAsync(
            claim,
            "ClaimStatusUpdated",
            c => new { c.Id, c.PolicyId, c.Status, c.DecisionDate, c.DecidedBy },
            cancellationToken);
    }

    /// <summary>
    ///     Rule identifier reported when a status transition is not legal.
    /// </summary>
    public const string IllegalTransitionRule = "illegal-claim-transition";

    /// <summary>
    ///     Deletes a claim that has not been adjudicated, and records an outbox message.
    /// </summary>
    /// <remarks>
    ///     An approved or denied claim is a decision: it carries who decided it, when, and the notes
    ///     they left, and deleting the row would erase that record while leaving the payout it settled
    ///     untouched. Such a claim is refused as a conflict instead. A claim still pending adjudication
    ///     has no decision behind it, so removing it destroys nothing that was ever decided and its
    ///     amount is released back to the policy's remaining coverage.
    /// </remarks>
    /// <param name="id">The claim identifier.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>True when a claim was found and deleted; false when no claim has that identifier.</returns>
    public async Task<bool> Delete(int id, CancellationToken cancellationToken = default)
    {
        var claim = await context.Claims.FindAsync([id], cancellationToken);
        if (claim == null) return false;

        if (claim.Status != ClaimStatus.Pending)
        {
            throw new ConflictException(
                $"Claim '{id}' has been adjudicated as {claim.Status} and can no longer be deleted.");
        }

        context.Claims.Remove(claim);

        await context.SaveWithOutboxAsync(
            claim,
            "ClaimDeleted",
            c => new { c.Id, c.PolicyId, c.Amount, c.Status },
            cancellationToken);

        return true;
    }

    /// <summary>
    ///     Whether a claim's amount counts against its policy's coverage limit.
    /// </summary>
    /// <remarks>
    ///     Pending and approved claims both reserve value that can still be paid out; only a denied
    ///     claim releases it. Counting only approved claims would let a caller file unlimited pending
    ///     claims against an exhausted policy and have every one denied afterwards.
    ///     <para>
    ///     Held as an <see cref="Expression{TDelegate}" /> rather than a method because the predicate is
    ///     used inside a <c>Where</c>: EF translates the expression tree it is handed, but it cannot
    ///     translate a call to a method, so the filter was being rejected and every claim creation
    ///     failed with an untranslated-lambda 500.
    ///     </para>
    /// </remarks>
    private static readonly Expression<Func<Claim, bool>> IsRecognisedAgainstCoverage
        = claim => claim.Status != ClaimStatus.Denied;
}
