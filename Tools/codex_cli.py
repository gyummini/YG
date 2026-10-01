"""
Shared Codex CLI settings for the image-generation scripts (gen_art_gpt, gen_entity_lofi, gen_pictograms).

- Binary: CODEX env var, else the newest Codex app CLI (%LOCALAPPDATA%/OpenAI/Codex/bin/*/codex.exe), else the old copy
  in %TEMP%/nocx. The app CLI follows the model set in ~/.codex/config.toml; older copies may not know it.
- Reasoning effort: CODEX_EFFORT env var, else the caller's default. Valid values (~/.codex/models_cache.json): low,
  medium, high, xhigh, max, ultra. The app's labels differ (its "Light" is presumably low); "light" is not a valid value.
  Draft-quality images need no more than medium; xhigh and above are almost never worth the usage.
"""
import glob
import os

TEMP = os.environ.get("TEMP", "/tmp")


def codex_path():
    if os.environ.get("CODEX"):
        return os.environ["CODEX"]
    found = glob.glob(os.path.join(os.environ.get("LOCALAPPDATA", ""), "OpenAI", "Codex", "bin", "*", "codex.exe"))
    if found:
        return max(found, key=os.path.getmtime)
    return os.path.join(TEMP, "nocx", "codex.exe")


def exec_args(effort="medium"):
    """`codex exec` prefix with the reasoning effort set; callers append -i/-s/-C and the prompt."""
    return [codex_path(), "exec", "--skip-git-repo-check", "--ephemeral",
            "-c", f"model_reasoning_effort={os.environ.get('CODEX_EFFORT', effort)}"]
