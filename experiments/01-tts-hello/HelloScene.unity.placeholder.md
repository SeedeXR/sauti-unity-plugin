# HelloScene.unity — Manual scene creation steps

Unity scene files are YAML emitted by the Editor and cannot be reliably authored by hand. Build this scene on first open:

1. **File → New Scene** (Basic Built-in, Empty if asked).
2. Right-click the Hierarchy → **Create Empty**, rename it `KokoroHello`.
3. With `KokoroHello` selected, **Inspector → Add Component → Sauti / Experiments / Tts Hello / Kokoro Hello**.
4. Add Component → **Audio Source**. Uncheck **Play On Awake**.
5. In the `Kokoro Hello` component, leave **Text To Speak** at its default for the first run.
6. **File → Save As** → `experiments/01-tts-hello/HelloScene.unity`.
7. Press **Play** to verify the scaffold logs in the Console.

Once `KOKORO-DL-001` and `TTS-API-001` land, this scene will actually produce audio.
