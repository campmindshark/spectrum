using System.Collections.Generic;
using System.Threading.Tasks;
using Spectrum.Base;

namespace Spectrum.Web {

  /**
   * The web control for saved dome scenes, mirroring LayersController: every
   * mutation funnels through the application-state dispatcher so the read of the live stack
   * (on save) and the config writes land on the serialization thread, exactly
   * like a native GUI write. The scene *list* is broadcast on the SSE "scenes"
   * frame so every client's dropdown converges; applying a scene broadcasts one
   * versioned "show" frame containing the stack, palettes, and both globals.
   *
   * The actual logic lives in the shared SceneService (Base), so this surface and
   * the native DomeScenesController can't diverge.
   */
  public sealed class SceneController {

    // The list GET returns: just the saved scene names, in stored order.
    public sealed class ScenesState {
      public IReadOnlyList<string> scenes { get; set; }
    }

    private readonly ApplicationStateDispatcher gateway;
    private readonly SceneService service;

    public SceneController(
      ApplicationStateDispatcher gateway, Configuration config
    ) {
      this.gateway = gateway;
      this.service = new SceneService(config, DomeLayerCatalog.Metadata);
    }

    public ScenesState State() {
      return new ScenesState { scenes = this.service.Names() };
    }

    // Save the current stack + globals under `name` (overwriting an existing
    // scene with that name). The whole save — reading the live stack and swapping
    // domeScenes — runs on the serialization thread inside the gateway action.
    public async Task<(bool ok, string error)> SaveAsync(string name) {
      (bool ok, string error) result = (false, "not run");
      await this.gateway.InvokeAsync(() => result = this.service.Save(name));
      return result;
    }

    public async Task<(bool ok, string error)> ApplyAsync(string name) {
      (bool ok, string error) result = (false, "not run");
      await this.gateway.InvokeAsync(() => result = this.service.Apply(name));
      return result;
    }

    public async Task<(bool ok, string error)> DeleteAsync(string name) {
      (bool ok, string error) result = (false, "not run");
      await this.gateway.InvokeAsync(() => result = this.service.Delete(name));
      return result;
    }
  }
}
