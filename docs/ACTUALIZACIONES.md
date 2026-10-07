# Actualizaciones de LocalInfantil

## Estado y límites

F10 implementa la estructura técnica del actualizador, pero no hay un canal público configurado ni una clave de firma productiva. La pantalla debe indicar **Canal de actualizaciones no configurado** hasta que se proporcionen ambos. La descarga y verificación no equivalen a publicar ni instalar una versión. La instalación es manual, confirmada por la Dueña y bloqueada mientras la caja o una operación de backup/restauración esté activa. En `--dev-db`, toda operación de actualización está deshabilitada y no se crea ni consulta el almacenamiento productivo `Updates/`.

La versión de preproducción es `0.9.0` y procede de `<Version>` en el proyecto WPF. No representa una versión aprobada para producción. El schema vigente es V16, agregado por F12/D-48 para persistencia durable y reconciliación de operaciones Mercado Pago Point; no guarda credenciales ni Access Token. F10 no agrega migraciones para configuración o actualizaciones. Producción NO habilitada.

## Manifiesto firmado

El publicador aloja por HTTPS un JSON `manifest.json` con `FormatoManifiesto`, `Version`, `UrlDescarga`, `Sha256`, `TamanoBytes`, `PublicadoEn`, `VersionMinimaSoportada`, `NotasVersion` y `Firma` (Base64 Ed25519 detached). La firma cubre todos los campos lógicos mediante el protocolo canónico documentado en D-46: dominio `LocalInfantil.UpdateManifest\0canonical-v1` y ocho campos UTF-8 con longitud Int32 big-endian. Alterar notas, versión, URL, tamaño o cualquier otro dato invalida la firma.

El cliente valida HTTPS, versión y mínimo soportado, firma pública, tamaño exacto y SHA-256. Descarga en `Updates/staging/*.partial`; solo después de validar se promueve a `Updates/listos/` junto a un manifiesto asociado. En el próximo inicio vuelve a verificar firma e integridad para mostrar la versión lista. Los `.partial` se limpian al inicio; un fallo conserva la app instalada y no dispara rollback ni instalación automática.

## Publicar un canal (aún no configurado)

1. Confirmar con la Dueña/administración que el release está aprobado y que la versión/paquete corresponden a la arquitectura soportada.
2. Generar la clave Ed25519 en un almacén seguro externo. Nunca guardar la clave privada en Git, el instalador, `%LOCALAPPDATA%\LocalInfantil`, logs o tickets. Restringir acceso y tener un procedimiento independiente de rotación/revocación.
3. Configurar la clave pública y URL HTTPS del canal en el equipo de producción usando un procedimiento de distribución aprobado. `canal.json` no se incluye con una clave/URL ficticia.
4. Construir Release y el instalador con `installer/Build-Installer.ps1`; revisar publicación y contenido antes de firmar.
5. Crear manifiesto y firma con `tools/Crear-ManifiestoActualizacion.ps1`. El archivo de clave indicado debe permanecer fuera del repositorio y contener la clave secreta Ed25519 en Base64 (64 bytes). El script toma la versión única desde el `.csproj` y produce `manifest.json` y `manifest.sig`.
6. Publicar ambos manifiestos y el instalador bajo HTTPS, probar con una cuenta/equipo aislado y revisar estado, descarga, cancelación, integridad y puerta de instalación antes de habilitar a usuarios.

Ejemplo orientativo (reemplazar URLs, fecha, notas y rutas por valores reales aprobados):

```powershell
.\tools\Crear-ManifiestoActualizacion.ps1 `
  -Instalador 'C:\release\LocalInfantil-Setup-0.9.0-net10-win-x64.exe' `
  -UrlHttps 'https://releases.example.invalid/LocalInfantil-Setup-0.9.0.exe' `
  -PublicadoUtc '2026-10-07T15:00:00Z' `
  -VersionMinima '0.9.0' `
  -Notas 'Versión de prueba' `
  -ClavePrivadaFueraDelRepo 'C:\secure-store\localinfantil-ed25519.txt' `
  -Salida 'C:\release\channel'
```

Este ejemplo no crea un canal, dominio, release ni clave. No usar una URL `.invalid` en una publicación real. Los canales actuales solo permiten HTTPS; HTTP local se limita a tests explícitos.

## Instalación segura y soporte

La usuaria elige **Instalar actualización** y confirma. Application exige `actualizaciones.instalar` para la sesión activa; la matriz predeterminada concede a Dueña y deniega a Empleada. La puerta toma `BEGIN IMMEDIATE`, vuelve a comprobar caja cerrada y locks de backup/restauración, y conserva el bloqueo temporal hasta cancelar o iniciar el instalador. No se cierra la caja automáticamente. Si cancelar o fallar el lanzamiento, se libera el bloqueo y continúa la versión actual.

No instalar en la PC del local hasta completar F00-T02, F10-T01–T05, F11-F, F08 OAuth/Drive/restore y aprobación de fiscalidad/permisos. Ante una actualización fallida, no borrar `Data`, `Backups` ni `Secrets`; recopilar versión, estado y logs técnicos sin contraseñas/tokens y escalar a quien administra el proyecto. Seguir `docs/CHECKLIST_PC_REAL.md` y `docs/PRUEBA_PILOTO.md`.
