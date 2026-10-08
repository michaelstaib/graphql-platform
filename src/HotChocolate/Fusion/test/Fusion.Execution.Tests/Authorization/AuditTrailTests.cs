using System.Collections.Immutable;
using System.Security.Claims;
using HotChocolate.Execution;
using HotChocolate.Fusion.Authorization.Audit;
using HotChocolate.Fusion.Authorization.InMemory;
using HotChocolate.Fusion.Configuration;
using HotChocolate.Language;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Fusion.Authorization;

public class AuditTrailTests : AuthorizationExecutionTestBase
{
    private const string Schema =
        """
        type Query {
          plain: String
          secret: String @authenticated
          scoped: String @requiresScopes(scopes: [["read", "write"], ["admin"]])
          guarded(id: ID!): String @policy(policies: [["finance"]])
          silent: String @policy(policies: [["silent"]])
          audited: String @policy(policies: [["audited"]])
          owned(id: ID!): String @policy(policies: [["owner"]])
        }
        """;

    private const string GhostSchema =
        """
        type Query {
          ghost: String @policy(policies: [["ghost"]])
        }
        """;

    private const string Data =
        """
        {
          "plain": "p",
          "secret": "s",
          "scoped": "sc",
          "guarded": "g",
          "silent": "si",
          "audited": "a",
          "owned": "o",
          "ghost": "gh"
        }
        """;

    [Fact]
    public async Task BeginRequest_Should_RecordEntryOfEveryDirective_When_OperationIsProtected()
    {
        // arrange
        var client = new AuthorizationTestClient(Data);
        var audit = new RecordingAuditProvider(client);
        var executor = await CreateAuditedExecutorAsync(
            Schema,
            client,
            audit);
        var user = Authenticated(new Claim("scope", "read"));

        // act
        await using var result = await executor.ExecuteAsync(
            CreateRequest("{ secret scoped guarded(id: \"7\") silent }", user).Build(),
            TestContext.Current.CancellationToken);

        // assert
        var scope = Assert.Single(audit.Scopes);
        scope.Entries.Select(Format).MatchInlineSnapshots(
            [
                "Query.secret | @authenticated | scopes=[] | args={} | Allowed | reason=- | data=-",
                "Query.scoped | @requiresScopes | scopes=[[admin],[read,write]] | args={} | Denied | reason=- | data=-",
                "Query.guarded | @policy(finance) | scopes=[] | args={id:\"7\"} | Allowed | reason=- | data=-",
                "Query.silent | @policy(silent) | scopes=[] | args={} | Unanswered | reason=- | data=-"
            ]);
    }

    [Fact]
    public async Task BeginRequest_Should_RecordReasonAndAuditData_When_PolicyDeniesWithBoth()
    {
        // arrange
        var client = new AuthorizationTestClient(Data);
        var audit = new RecordingAuditProvider(client);
        var executor = await CreateAuditedExecutorAsync(
            Schema,
            client,
            audit);

        // act
        await using var result = await executor.ExecuteAsync(
            CreateRequest("{ audited }", Authenticated()).Build(),
            TestContext.Current.CancellationToken);

        // assert
        var scope = Assert.Single(audit.Scopes);
        scope.Entries.Select(Format).MatchInlineSnapshots(
            [
                "Query.audited | @policy(audited) | scopes=[] | args={} | Denied | reason=not the owner | data={rule:owner}"
            ]);
    }

    [Fact]
    public async Task BeginRequest_Should_RecordUnauthenticatedDenials_When_PrincipalIsAnonymous()
    {
        // arrange
        var client = new AuthorizationTestClient(Data);
        var audit = new RecordingAuditProvider(client);
        var executor = await CreateAuditedExecutorAsync(Schema, client, audit);

        // act
        await using var result = await executor.ExecuteAsync(
            CreateRequest("{ secret scoped guarded(id: \"7\") }").Build(),
            TestContext.Current.CancellationToken);

        // assert
        var scope = Assert.Single(audit.Scopes);
        scope.Entries.Select(Format).MatchInlineSnapshots(
            [
                "Query.scoped | @requiresScopes | scopes=[[admin],[read,write]] | args={} | Denied | reason=unauthenticated | data=-",
                "Query.guarded | @policy(finance) | scopes=[] | args={id:\"7\"} | Denied | reason=unauthenticated | data=-",
                "Query.secret | @authenticated | scopes=[] | args={} | Denied | reason=unauthenticated | data=-"
            ]);
    }

    [Fact]
    public async Task BeginRequest_Should_RecordFailureReason_When_PolicyIsUnresolved()
    {
        // arrange
        var client = new AuthorizationTestClient(Data);
        var audit = new RecordingAuditProvider(client);
        var executor = await CreateAuditedExecutorAsync(
            GhostSchema,
            client,
            audit,
            configure: builder => builder.ModifyAuthorizationOptions(
                options => options.DisableAuthorizationValidation = true));

        // act
        await using var result = await executor.ExecuteAsync(
            CreateRequest("{ ghost }").Build(),
            TestContext.Current.CancellationToken);

        // assert
        var scope = Assert.Single(audit.Scopes);
        scope.Entries.Select(Format).MatchInlineSnapshots(
            [
                "Query.ghost | @policy(ghost) | scopes=[] | args={} | Unanswered | reason=No policy provider knows the policy 'ghost' of the directive '@policy'. | data=-"
            ]);
        Assert.Equal(
            "System.InvalidOperationException: No policy provider knows the policy 'ghost' of the directive '@policy'.",
            scope.FailureReason);
        Assert.Equal(1, scope.CommitCount);
        Assert.Empty(client.Requests);
    }

    [Fact]
    public async Task ExecuteAsync_Should_CommitScopeWithFailureReason_When_PolicyThrows()
    {
        // arrange
        var client = new AuthorizationTestClient(Data);
        var audit = new RecordingAuditProvider(client);
        var executor = await CreateAuditedExecutorAsync(
            Schema,
            client,
            audit,
            policies => policies.Evaluate("owner", (_, _) => throw new PolicyFault("boom")));

        // act
        await using var result = await executor.ExecuteAsync(
            CreateRequest("{ secret owned(id: \"1\") guarded(id: \"7\") }", Authenticated()).Build(),
            TestContext.Current.CancellationToken);

        // assert
        var scope = Assert.Single(audit.Scopes);
        scope.Entries.Select(Format).MatchInlineSnapshots(
            [
                "Query.secret | @authenticated | scopes=[] | args={} | Allowed | reason=- | data=-",
                "Query.owned | @policy(owner) | scopes=[] | args={id:\"1\"} | Unanswered | reason=- | data=-",
                "Query.guarded | @policy(finance) | scopes=[] | args={id:\"7\"} | Unanswered | reason=- | data=-"
            ]);
        Assert.Equal("HotChocolate.Fusion.Authorization.AuditTrailTests+PolicyFault: boom", scope.FailureReason);
        Assert.Equal(1, scope.CommitCount);
        result.MatchInlineSnapshot(
            """
            {
              "errors": [
                {
                  "message": "Unexpected Execution Error"
                }
              ]
            }
            """);
        Assert.Empty(client.Requests);
    }

    [Fact]
    public async Task ExecuteAsync_Should_LeaveScopeUncommitted_When_EvaluationIsCanceled()
    {
        // arrange
        var client = new AuthorizationTestClient(Data);
        var audit = new RecordingAuditProvider(client);
        var executor = await CreateAuditedExecutorAsync(
            Schema,
            client,
            audit,
            policies => policies.Evaluate("owner", (_, _) => throw new OperationCanceledException()));

        // act
        await using var result = await executor.ExecuteAsync(
            CreateRequest("{ owned(id: \"1\") }", Authenticated()).Build(),
            TestContext.Current.CancellationToken);

        // assert
        Assert.Equal(0, Assert.Single(audit.Scopes).CommitCount);
    }

    [Fact]
    public async Task BeginRequest_Should_CaptureSubjectAndProviderContext_When_ScopeIsOpened()
    {
        // arrange
        var client = new AuthorizationTestClient(Data);
        var audit = new RecordingAuditProvider(client);
        var executor = await CreateAuditedExecutorAsync(Schema, client, audit);
        var user = Authenticated(
            new Claim("sub", "u1"),
            new Claim("scope", "read"));

        // act
        await using var result = await executor.ExecuteAsync(
            CreateRequest("{ secret }", user).Build(),
            TestContext.Current.CancellationToken);

        // assert
        var scope = Assert.Single(audit.Scopes);
        Format(scope.Subject).MatchInlineSnapshot(
            "authenticated=True | name=- | type=test | claims=[sub=u1,scope=read]");
        Assert.Equal("1", Assert.Single(scope.Context).Value);
    }

    [Fact]
    public async Task BeginRequest_Should_CarryOperationAndPlanIdOfThePolicyContext_When_ScopeIsOpened()
    {
        // arrange
        var client = new AuthorizationTestClient(Data);
        var audit = new RecordingAuditProvider(client);
        (string OperationId, string PlanId)? evaluated = null;
        var executor = await CreateAuditedExecutorAsync(
            Schema,
            client,
            audit,
            policies => policies.Evaluate(
                "owner",
                (context, _) =>
                {
                    evaluated = (context.Plan.Operation.Id, context.Plan.Id);
                    return true;
                }));

        // act
        await using var result = await executor.ExecuteAsync(
            CreateRequest("{ owned(id: \"1\") }", Authenticated()).Build(),
            TestContext.Current.CancellationToken);

        // assert
        var info = Assert.Single(audit.Scopes).Info;
        Assert.Equal(evaluated, (info.OperationId, info.PlanId));
        Assert.NotEmpty(info.OperationId);
    }

    [Fact]
    public async Task BeginRequest_Should_OpenOneScopePerVariableSet_When_RequestIsVariableBatch()
    {
        // arrange
        var client = new AuthorizationTestClient(Data);
        var audit = new RecordingAuditProvider(client);
        var executor = await CreateAuditedExecutorAsync(
            Schema,
            client,
            audit,
            policies => policies.Evaluate(
                "owner",
                (_, entry) => entry.Arguments["id"] is StringValueNode { Value: "1" }));
        var request = CreateRequest("query($id: ID!) { owned(id: $id) }", Authenticated())
            .SetVariableValues("""[{"id":"1"},{"id":"2"}]""")
            .Build();

        // act
        await using var result = await executor.ExecuteAsync(
            request,
            TestContext.Current.CancellationToken);

        // assert
        Assert.Equal(1, audit.TrailCount);
        audit.Scopes.Select(Format).MatchInlineSnapshots(
            [
                "trail=1 | request=-1 | set=0 | Query.owned @policy(owner) args={id:\"1\"} Allowed",
                "trail=1 | request=-1 | set=1 | Query.owned @policy(owner) args={id:\"2\"} Denied"
            ]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BeginRequest_Should_RecordEachOccurrenceOnce_When_DeferIsConditional(bool defer)
    {
        // arrange
        var client = new AuthorizationTestClient(Data);
        var audit = new RecordingAuditProvider(client);
        var executor = await CreateAuditedExecutorAsync(Schema, client, audit);
        var request = CreateRequest(
                "query($d: Boolean!) { plain ... @defer(if: $d) { owned(id: \"1\") } }",
                Authenticated())
            .SetVariableValues($$"""{"d":{{(defer ? "true" : "false")}}}""")
            .Build();

        // act
        await using var result = await executor.ExecuteAsync(
            request,
            TestContext.Current.CancellationToken);

        // assert
        var scope = Assert.Single(audit.Scopes);
        scope.Entries.Select(Format).MatchInlineSnapshots(
            [
                "Query.owned | @policy(owner) | scopes=[] | args={id:\"1\"} | Allowed | reason=- | data=-"
            ]);
    }

    [Fact]
    public async Task ExecuteBatchAsync_Should_CreateTrailOnce_When_RequestsAreBatched()
    {
        // arrange
        var client = new AuthorizationTestClient(Data);
        var audit = new RecordingAuditProvider(client);
        var executor = await CreateAuditedExecutorAsync(Schema, client, audit);
        var batch = new OperationRequestBatch(
            [
                CreateRequest("{ secret }", Authenticated()).Build(),
                CreateRequest("{ plain }", Authenticated()).Build(),
                CreateRequest("{ secret }", Authenticated()).Build()
            ]);

        // act
        await using var stream = await executor.ExecuteBatchAsync(
            batch,
            TestContext.Current.CancellationToken);
        await foreach (var item in stream.ReadResultsAsync().WithCancellation(TestContext.Current.CancellationToken))
        {
            await item.DisposeAsync();
        }

        // assert
        Assert.Equal(1, audit.TrailCount);
        audit.Scopes.OrderBy(s => s.Info.RequestIndex).Select(Format).MatchInlineSnapshots(
            [
                "trail=1 | request=0 | set=0 | Query.secret @authenticated args={} Allowed",
                "trail=1 | request=2 | set=0 | Query.secret @authenticated args={} Allowed"
            ]);
    }

    [Fact]
    public async Task ExecuteBatchAsync_Should_GiveEveryScopeTheSameInvocationId_When_RequestsAreBatched()
    {
        // arrange
        var client = new AuthorizationTestClient(Data);
        var audit = new RecordingAuditProvider(client);
        var executor = await CreateAuditedExecutorAsync(Schema, client, audit);
        var batch = new OperationRequestBatch(
            [
                CreateRequest("{ secret }", Authenticated()).Build(),
                CreateRequest("{ secret }", Authenticated()).Build()
            ]);

        // act
        await using var stream = await executor.ExecuteBatchAsync(
            batch,
            TestContext.Current.CancellationToken);
        await foreach (var item in stream.ReadResultsAsync().WithCancellation(TestContext.Current.CancellationToken))
        {
            await item.DisposeAsync();
        }

        // assert
        audit.Scopes.OrderBy(s => s.Info.RequestIndex).Select(s => s.Trail.InvocationId).MatchInlineSnapshots(
            [
                "invocation-1",
                "invocation-1"
            ]);
    }

    [Fact]
    public async Task ExecuteAsync_Should_NotCreateTrail_When_OperationIsUnprotected()
    {
        // arrange
        var client = new AuthorizationTestClient(Data);
        var audit = new RecordingAuditProvider(client);
        var executor = await CreateAuditedExecutorAsync(Schema, client, audit);

        // act
        await using var result = await executor.ExecuteAsync(
            CreateRequest("{ plain }", Authenticated()).Build(),
            TestContext.Current.CancellationToken);

        // assert
        result.MatchInlineSnapshot(
            """
            {
              "data": {
                "plain": "p"
              }
            }
            """);
        Assert.Equal(0, audit.TrailCount);
    }

    [Fact]
    public async Task ExecuteBatchAsync_Should_NotCreateTrail_When_EveryOperationIsUnprotected()
    {
        // arrange
        var client = new AuthorizationTestClient(Data);
        var audit = new RecordingAuditProvider(client);
        var executor = await CreateAuditedExecutorAsync(Schema, client, audit);
        var batch = new OperationRequestBatch(
            [
                CreateRequest("{ plain }", Authenticated()).Build(),
                CreateRequest("{ plain }", Authenticated()).Build()
            ]);

        // act
        await using var stream = await executor.ExecuteBatchAsync(
            batch,
            TestContext.Current.CancellationToken);
        await foreach (var item in stream.ReadResultsAsync().WithCancellation(TestContext.Current.CancellationToken))
        {
            await item.DisposeAsync();
        }

        // assert
        Assert.Equal(0, audit.TrailCount);
    }

    [Fact]
    public async Task CommitAsync_Should_RunOnceBeforeTheSourceSchemaIsCalled_When_OperationIsProtected()
    {
        // arrange
        var client = new AuthorizationTestClient(Data);
        var audit = new RecordingAuditProvider(client);
        var executor = await CreateAuditedExecutorAsync(Schema, client, audit);

        // act
        await using var result = await executor.ExecuteAsync(
            CreateRequest("{ secret }", Authenticated()).Build(),
            TestContext.Current.CancellationToken);

        // assert
        var scope = Assert.Single(audit.Scopes);
        Assert.Equal(1, scope.CommitCount);
        Assert.Equal(0, scope.SourceSchemaRequestsAtCommit);
        Assert.Single(client.Requests);
    }

    [Fact]
    public async Task CommitAsync_Should_RunBeforeTheRequestIsRejected_When_DenialEscalates()
    {
        // arrange
        var client = new AuthorizationTestClient(Data);
        var audit = new RecordingAuditProvider(client);
        var executor = await CreateAuditedExecutorAsync(
            Schema,
            client,
            audit,
            configure: builder => builder.ModifyAuthorizationOptions(
                options => options.RejectRequestOn = RejectRequestOn.OnUnauthenticated));

        // act
        await using var result = await executor.ExecuteAsync(
            CreateRequest("{ secret }").Build(),
            TestContext.Current.CancellationToken);

        // assert
        var scope = Assert.Single(audit.Scopes);
        Assert.Equal(1, scope.CommitCount);
        scope.Entries.Select(Format).MatchInlineSnapshots(
            [
                "Query.secret | @authenticated | scopes=[] | args={} | Denied | reason=unauthenticated | data=-"
            ]);
        Assert.Empty(client.Requests);
    }

    [Fact]
    public async Task Record_Should_Throw_When_ScopeIsCommitted()
    {
        // arrange
        var scope = CreateScope();
        await scope.CommitAsync(TestContext.Current.CancellationToken);
        var entry = CreateEntry();

        // act
        var exception = Assert.Throws<InvalidOperationException>(() => scope.Record(entry));

        // assert
        Assert.Equal(
            "The audit scope is already committed and cannot be changed or committed again.",
            exception.Message);
    }

    [Fact]
    public async Task CommitAsync_Should_Throw_When_ScopeIsCommittedTwice()
    {
        // arrange
        var scope = CreateScope();
        scope.Record(CreateEntry());
        await scope.CommitAsync(TestContext.Current.CancellationToken);

        // act
        var exception = Assert.Throws<InvalidOperationException>(
            () => scope.CommitAsync(TestContext.Current.CancellationToken));

        // assert
        Assert.Equal(
            "The audit scope is already committed and cannot be changed or committed again.",
            exception.Message);
        Assert.Equal(1, scope.CommitCount);
    }

    [Fact]
    public async Task CommitAsync_Should_HandOverEntriesInOrder_When_ScopeIsCommitted()
    {
        // arrange
        var scope = CreateScope();
        scope.Record(CreateEntry() with { DirectiveName = DirectiveNames.Authenticated.Name });
        scope.Record(CreateEntry() with { DirectiveName = DirectiveNames.RequiresScopes.Name });

        // act
        await scope.CommitAsync(TestContext.Current.CancellationToken);

        // assert
        Assert.Equal([DirectiveNames.Authenticated.Name, DirectiveNames.RequiresScopes.Name], scope.Entries.Select(e => e.DirectiveName));
    }

    [Fact]
    public async Task Gateway_Should_UseNoOpAuditProvider_When_NoProviderIsRegistered()
    {
        // arrange
        var client = new AuthorizationTestClient(Data);
        var executor = await CreateExecutorAsync(
            Schema,
            client,
            new InMemoryPolicyRecorder(),
            policies => policies.Unanswered("silent"),
            builder => builder.ConfigureSchemaServices(
                (_, sc) => sc.AddSingleton<IPolicyProvider>(new AuditDataPolicy())));
        var provider = executor.Schema.Services.GetRequiredService<IAuditProvider>();
        var user = Authenticated();
        var info = new AuditScopeInfo("operation", "plan", -1, 0);

        // act
        await using var result = await executor.ExecuteAsync(
            CreateRequest("{ secret }", user).Build(),
            TestContext.Current.CancellationToken);
        var first = provider.CreateTrail(executor.Schema.Services).BeginRequest(info, user);
        var second = provider.CreateTrail(executor.Schema.Services).BeginRequest(info, user);

        // assert
        result.MatchInlineSnapshot(
            """
            {
              "data": {
                "secret": "s"
              }
            }
            """);
        Assert.False(first.IsRecording);
        Assert.Same(first, second);
    }

    private static Task<IRequestExecutor> CreateAuditedExecutorAsync(
        string schema,
        AuthorizationTestClient client,
        RecordingAuditProvider audit,
        Action<InMemoryPolicyBuilder>? policies = null,
        Action<IFusionGatewayBuilder>? configure = null)
        => CreateExecutorAsync(
            schema,
            client,
            new InMemoryPolicyRecorder(),
            configured =>
            {
                configured.Unanswered("silent");
                policies?.Invoke(configured);
            },
            builder =>
            {
                builder.ConfigureSchemaServices(
                    (_, sc) =>
                    {
                        sc.AddSingleton<IAuditProvider>(audit);
                        sc.AddSingleton<IPolicyProvider>(new AuditDataPolicy());
                    });
                configure?.Invoke(builder);
            });

    private sealed class PolicyFault(string message) : Exception(message);

    private static RecordingAuditScope CreateScope()
        => new(
            NoOpAuditTrail.Instance,
            new AuditScopeInfo("operation", "plan", -1, 0),
            Authenticated(),
            ImmutableDictionary.Create<string, string>(),
            static () => 0);

    private static AuditLogEntry CreateEntry()
        => new(
            new SchemaCoordinate("Query", "secret"),
            DirectiveNames.Authenticated.Name,
            null,
            [],
            ImmutableDictionary.Create<string, string>(),
            PolicyOutcome.Allowed,
            null,
            null);

    private static string Format(AuditLogEntry entry)
    {
        var policyName = entry.PolicyName is null ? string.Empty : $"({entry.PolicyName})";
        var scopes = string.Join(",", entry.Scopes.Select(group => $"[{string.Join(",", group)}]"));
        var arguments = string.Join(",", entry.Arguments.Select(a => $"{a.Key}:{a.Value}"));
        var data = entry.AuditData is null
            ? "-"
            : "{" + string.Join(",", entry.AuditData.Select(a => $"{a.Key}:{a.Value}")) + "}";

        return $"{entry.Coordinate} | @{entry.DirectiveName}{policyName} | scopes=[{scopes}] "
            + $"| args={{{arguments}}} | {entry.Outcome} | reason={entry.Reason ?? "-"} | data={data}";
    }

    private static string Format(AuditSubject subject)
    {
        var claims = string.Join(",", subject.Claims.Select(c => $"{c.Type}={c.Value}"));

        return $"authenticated={subject.IsAuthenticated} | name={subject.Name ?? "-"} "
            + $"| type={subject.AuthenticationType ?? "-"} | claims=[{claims}]";
    }

    private static string Format(RecordingAuditScope scope)
    {
        var info = scope.Info;
        var entries = string.Join(
            ";",
            scope.Entries.Select(
                e => $"{e.Coordinate} @{e.DirectiveName}{(e.PolicyName is null ? string.Empty : $"({e.PolicyName})")} "
                    + $"args={{{string.Join(",", e.Arguments.Select(a => $"{a.Key}:{a.Value}"))}}} {e.Outcome}"));

        return $"trail={scope.Context["trail"]} | request={info.RequestIndex} | set={info.VariableSetIndex} | {entries}";
    }
}
