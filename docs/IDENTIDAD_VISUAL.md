# Identidad visual

El logo oficial de TiNA kids se mantiene en `src/LocalInfantil.Wpf/Assets/Branding/logo-tina.png` y se empaqueta como recurso WPF dentro de la aplicación, por lo que viaja también en las publicaciones.

Para reemplazarlo en el futuro, conservar ese nombre y ubicación, mantener el PNG transparente y respetar su proporción. `gemini-svg.svg` en la misma carpeta queda como fuente vectorial original. La infraestructura expone los bytes mediante `LogoNegocio.ObtenerPng()` para los renderizadores de documentos; las vistas XAML usan el mismo recurso empaquetado.

El banner horizontal está en `src/LocalInfantil.Wpf/Assets/Branding/logo.png` y también se empaqueta como recurso WPF. Los renderizadores pueden obtener sus bytes con `LogoNegocio.ObtenerBannerPng()`. Los reportes disponen de documento A4 para vista previa e impresión; la generación de PDF depende de seleccionar una impresora PDF de Windows. Los comprobantes siguen siendo internos y no fiscales.
