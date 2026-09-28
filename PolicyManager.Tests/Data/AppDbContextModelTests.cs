using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using PolicyManager.Models;
using PolicyManager.Tests.Infrastructure;

namespace PolicyManager.Tests.Data;

/// <summary>
///     Tests for the relational rules the model itself declares.
/// </summary>
/// <remarks>
///     The in-memory provider ignores both cascade and restrict behaviour when it deletes a row, so
///     asserting on the outcome of a delete there proves nothing. What can be asserted without a
///     database is the declaration the migration is generated from: if the foreign keys here stop
///     restricting, the SQL Server constraint stops protecting the claims, and the tests that do
///     exercise the behaviour against a real server stop describing the schema that ships.
/// </remarks>
public class AppDbContextModelTests : ServiceTestBase
{
    /// <summary>
    ///     A policy must not take its policyholder with it when it is deleted.
    /// </summary>
    /// <remarks>
    ///     Cascading here is what made a holder delete reach the claims two levels down.
    /// </remarks>
    [Fact]
    public void Policy_to_its_policyholder_does_not_cascade()
    {
        Assert.Equal(DeleteBehavior.Restrict, DeleteBehaviour(typeof(Policy), nameof(Policy.PolicyHolderId)));
    }

    /// <summary>
    ///     A claim must not be destroyed when the policy it was filed against is deleted.
    /// </summary>
    [Fact]
    public void Claim_to_its_policy_does_not_cascade()
    {
        Assert.Equal(DeleteBehavior.Restrict, DeleteBehaviour(typeof(Claim), nameof(Claim.PolicyId)));
    }

    /// <summary>
    ///     Reads the delete behaviour the model declares for one foreign key property.
    /// </summary>
    /// <param name="dependent">The entity declaring the foreign key.</param>
    /// <param name="propertyName">The name of the foreign key property.</param>
    /// <returns>The delete behaviour configured for that relationship.</returns>
    private DeleteBehavior DeleteBehaviour(Type dependent, string propertyName)
    {
        var entityType = Context.Model.FindEntityType(dependent);
        Assert.NotNull(entityType);

        var foreignKey = entityType!.GetForeignKeys().Single(fk => fk.Properties.Single().Name == propertyName);

        return foreignKey.DeleteBehavior;
    }
}
