using NetBox.Core.Services;
using Xunit;

namespace NetBox.Tests;

public sealed class RuntimeSessionStateMachineTests
{
  [Theory]
  [InlineData(RuntimeSessionState.Pending, RuntimeSessionState.Launching)]
  [InlineData(RuntimeSessionState.Pending, RuntimeSessionState.Failed)]
  [InlineData(RuntimeSessionState.Pending, RuntimeSessionState.Stopping)]
  [InlineData(RuntimeSessionState.Pending, RuntimeSessionState.Stopped)]
  [InlineData(RuntimeSessionState.Launching, RuntimeSessionState.Running)]
  [InlineData(RuntimeSessionState.Launching, RuntimeSessionState.Failed)]
  [InlineData(RuntimeSessionState.Launching, RuntimeSessionState.Stopping)]
  [InlineData(RuntimeSessionState.Launching, RuntimeSessionState.Stopped)]
  [InlineData(RuntimeSessionState.Running, RuntimeSessionState.Stopping)]
  [InlineData(RuntimeSessionState.Running, RuntimeSessionState.Stopped)]
  [InlineData(RuntimeSessionState.Running, RuntimeSessionState.Failed)]
  [InlineData(RuntimeSessionState.Stopping, RuntimeSessionState.Stopped)]
  [InlineData(RuntimeSessionState.Stopping, RuntimeSessionState.Failed)]
  public void EnsureValidTransition_AllowsLegalTransitions(RuntimeSessionState from, RuntimeSessionState to)
  {
    var exception = Record.Exception(() => RuntimeSessionStateMachine.EnsureValidTransition(from, to));
    Assert.Null(exception);
  }

  [Theory]
  [InlineData(RuntimeSessionState.Pending, RuntimeSessionState.Running)]
  [InlineData(RuntimeSessionState.Launching, RuntimeSessionState.Pending)]
  [InlineData(RuntimeSessionState.Running, RuntimeSessionState.Launching)]
  [InlineData(RuntimeSessionState.Stopping, RuntimeSessionState.Running)]
  [InlineData(RuntimeSessionState.Stopped, RuntimeSessionState.Running)]
  [InlineData(RuntimeSessionState.Stopped, RuntimeSessionState.Launching)]
  [InlineData(RuntimeSessionState.Failed, RuntimeSessionState.Running)]
  [InlineData(RuntimeSessionState.Failed, RuntimeSessionState.Stopped)]
  public void EnsureValidTransition_RejectsIllegalTransitions(RuntimeSessionState from, RuntimeSessionState to)
  {
    var exception = Assert.Throws<InvalidRuntimeStateTransitionException>(
      () => RuntimeSessionStateMachine.EnsureValidTransition(from, to));

    Assert.Equal(from, exception.From);
    Assert.Equal(to, exception.To);
  }

  [Theory]
  [InlineData(RuntimeSessionState.Pending)]
  [InlineData(RuntimeSessionState.Launching)]
  [InlineData(RuntimeSessionState.Running)]
  [InlineData(RuntimeSessionState.Stopping)]
  [InlineData(RuntimeSessionState.Stopped)]
  [InlineData(RuntimeSessionState.Failed)]
  public void EnsureValidTransition_AllowsSelfTransitionAsNoOp(RuntimeSessionState state)
  {
    var exception = Record.Exception(() => RuntimeSessionStateMachine.EnsureValidTransition(state, state));
    Assert.Null(exception);
  }

  [Theory]
  [InlineData("pending", RuntimeSessionState.Pending)]
  [InlineData("launching", RuntimeSessionState.Launching)]
  [InlineData("running", RuntimeSessionState.Running)]
  [InlineData("stopping", RuntimeSessionState.Stopping)]
  [InlineData("stopped", RuntimeSessionState.Stopped)]
  [InlineData("failed", RuntimeSessionState.Failed)]
  [InlineData("RUNNING", RuntimeSessionState.Running)]
  public void Parse_RoundTripsWireStrings(string wireValue, RuntimeSessionState expected)
  {
    Assert.Equal(expected, RuntimeSessionStateMachine.Parse(wireValue));
    Assert.Equal(wireValue.ToLowerInvariant(), RuntimeSessionStateMachine.ToWireString(expected));
  }

  [Fact]
  public void Parse_ThrowsForUnknownStatus()
  {
    Assert.Throws<ArgumentOutOfRangeException>(() => RuntimeSessionStateMachine.Parse("bogus"));
  }

  [Theory]
  [InlineData(RuntimeSessionState.Pending, true)]
  [InlineData(RuntimeSessionState.Launching, true)]
  [InlineData(RuntimeSessionState.Running, true)]
  [InlineData(RuntimeSessionState.Stopping, true)]
  [InlineData(RuntimeSessionState.Stopped, false)]
  [InlineData(RuntimeSessionState.Failed, false)]
  public void IsActive_MatchesActiveSessionQueryPredicate(RuntimeSessionState state, bool expectedActive)
  {
    Assert.Equal(expectedActive, RuntimeSessionStateMachine.IsActive(state));
    Assert.Equal(!expectedActive, RuntimeSessionStateMachine.IsTerminal(state));
  }

  [Theory]
  [InlineData(RuntimeSessionState.Pending, true)]
  [InlineData(RuntimeSessionState.Launching, true)]
  [InlineData(RuntimeSessionState.Running, true)]
  [InlineData(RuntimeSessionState.Stopping, false)]
  [InlineData(RuntimeSessionState.Stopped, false)]
  [InlineData(RuntimeSessionState.Failed, false)]
  public void CanResumeStream_ExcludesStoppingAndTerminalStates(RuntimeSessionState state, bool expectedResumable)
  {
    Assert.Equal(expectedResumable, RuntimeSessionStateMachine.CanResumeStream(state));
  }
}
