using System.Collections.Generic;
using DualSouls.Controllers;
using Xunit;

public sealed class ControllerBindingPlannerTests
{
    [Fact]
    public void MissingCurrentGamepadIsAttached()
    {
        var gamepad = new FakeGamepad("thor-pad");

        ControllerBindingPlan<FakeGamepad, FakeBinding> plan =
            ControllerBindingPlanner.Create(
                new[] { gamepad },
                new List<ControllerBinding<FakeGamepad, FakeBinding>>());

        Assert.Equal(new[] { gamepad }, plan.AttachGamepads);
        Assert.Empty(plan.DetachBindings);
    }

    [Fact]
    public void ReenumeratedGamepadDetachesStaleBindingAndAttachesReplacement()
    {
        var staleGamepad = new FakeGamepad("thor-pad");
        var currentGamepad = new FakeGamepad("thor-pad");
        var staleBinding = new FakeBinding();

        ControllerBindingPlan<FakeGamepad, FakeBinding> plan =
            ControllerBindingPlanner.Create(
                new[] { currentGamepad },
                new[]
                {
                    new ControllerBinding<FakeGamepad, FakeBinding>(
                        staleGamepad,
                        staleBinding,
                        recoveryOwned: false),
                });

        Assert.Equal(new[] { staleBinding }, plan.DetachBindings);
        Assert.Equal(new[] { currentGamepad }, plan.AttachGamepads);
    }

    [Fact]
    public void RecoveryBindingYieldsWhenBuiltInManagerCatchesUp()
    {
        var gamepad = new FakeGamepad("thor-pad");
        var recoveryBinding = new FakeBinding();
        var builtInBinding = new FakeBinding();

        ControllerBindingPlan<FakeGamepad, FakeBinding> plan =
            ControllerBindingPlanner.Create(
                new[] { gamepad },
                new[]
                {
                    new ControllerBinding<FakeGamepad, FakeBinding>(
                        gamepad,
                        recoveryBinding,
                        recoveryOwned: true),
                    new ControllerBinding<FakeGamepad, FakeBinding>(
                        gamepad,
                        builtInBinding,
                        recoveryOwned: false),
                });

        Assert.Equal(new[] { recoveryBinding }, plan.DetachBindings);
        Assert.Empty(plan.AttachGamepads);
    }

    sealed class FakeGamepad
    {
        public FakeGamepad(string name) { Name = name; }
        public string Name { get; }
    }

    sealed class FakeBinding
    {
    }
}
