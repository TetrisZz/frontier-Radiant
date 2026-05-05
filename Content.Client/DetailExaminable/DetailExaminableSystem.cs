using Content.Shared.DetailExaminable;

namespace Content.Client.DetailExaminable;

public sealed class DetailExaminableSystem : EntitySystem
{
    private DetailExaminableWindow? _window;

    public override void Initialize()
    {
        SubscribeLocalEvent<DetailExaminableOpenEvent>(OnDetailOpen);
    }

    private void OnDetailOpen(DetailExaminableOpenEvent ev)
    {
        if (!TryComp<DetailExaminableComponent>(ev.Target, out var detail))
            return;

        _window ??= new DetailExaminableWindow();
        _window.UpdateState(detail, ev.Target, EntityManager);

        _window.OpenCentered();
        _window.MoveToFront();
    }
}
