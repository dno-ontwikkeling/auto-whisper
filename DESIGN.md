# Design
Direction: calm utilitarian Windows 11 Fluent, dark only. Settings window, not a marketing surface.
Type: system Segoe UI Variable via wpf-ui / same, scale follows wpf-ui defaults (caption, body, subtitle, title).
Color: dark neutral base from wpf-ui theme, panels `#10FFFFFF`, inline chips `#18FFFFFF`, accent from wpf-ui system accent, semantic success/warning/error from wpf-ui.
Shape and depth: panels are `Border` with `CornerRadius="8"` and `Padding="16"`, flat translucent fills, no shadows.
Motion: restrained, ease-out on enter, 150-300 ms.
Reference: existing `Views/SettingsWindow.xaml` section rows. New sections must look like siblings of those.
Avoid: emoji in UI text, new colors or fonts, modal dialogs for simple settings, generic card grids.
