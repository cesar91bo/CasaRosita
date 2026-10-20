# Instalación y actualización en Windows

## Preparar una vez en la PC de desarrollo

Ejecutar pruebas y `scripts/Publish.ps1`. Copiar `artifacts/publish`, `scripts` y `docs` a USB. El local no necesita SDK ni Visual Studio. Para un asistente visual se incluye `installer/CasaRosita.iss`, compilable con Inno Setup 6; debe probarse en una VM Windows limpia antes de distribuirlo. El asistente exige SQL Express previo y propaga errores de la configuración.

## Requisitos del equipo del local

Windows x64 con navegador actualizado, SQL Server Express local instalado desde un paquete **offline oficial completo** y autenticación Windows. Instancia predeterminada para este proyecto: `SQLEXPRESS`. La versión utilizada en las pruebas se registra en VALIDACION.md; se recomienda una versión soportada y actualizada de SQL Server. Instalar el motor como servicio automático. No habilitar acceso remoto ni abrir el puerto de la aplicación en el firewall.

Un técnico con permisos de administrador Windows y sysadmin SQL realiza la instalación; las usuarias operan con una cuenta Windows estándar y los roles locales del sistema. Poner fecha/hora y zona horaria de Argentina. Configurar una impresora local o de red local en Windows, imprimir su página de prueba y establecerla como predeterminada. No se necesita una impresora fiscal.

## Instalar

Desde PowerShell **como administrador** en la carpeta del paquete USB:

```powershell
.\scripts\Install.ps1 -PublishFolder .\publish -Instance SQLEXPRESS -Port 5080
```

Rutas predeterminadas:

| Contenido | Ubicación |
| --- | --- |
| Versiones del programa | `C:\Program Files\CasaRosita\releases` |
| Configuración | `C:\ProgramData\CasaRosita\local.json` |
| Claves de sesión | `C:\ProgramData\CasaRosita\Keys` |
| Diagnóstico | `C:\ProgramData\CasaRosita\Logs` |
| Respaldos | `C:\ProgramData\CasaRosita\Backups` |
| Base de datos | Carpeta del motor SQL Server; nunca junto a los binarios |

El script valida manifiesto SHA256, instancia SQL, copia previa si hay base, migraciones, identidad virtual `NT SERVICE\CasaRosita`, ACL, acceso al motor, inicio del servicio y respuesta HTTP/SQL. Configura arranque automático, dependencia del motor y recuperación ante fallos del servicio. Crea el acceso directo «Casa Rosita» en el escritorio público. Se puede repetir conservando configuración, usuarios y datos. Cada ejecución deja una versión de binarios separada para recuperación.

La cuenta del servicio obtiene lectura/escritura y backup en la base, sin sysadmin. Esta primera versión necesita además `CREATE ANY DATABASE` para `RESTORE VERIFYONLY` de SQL Server; **no permite restaurar desde la aplicación**, pero este permiso del motor debe evaluarse en un endurecimiento posterior mediante procedimiento firmado dedicado. Las usuarias no reciben credenciales SQL ni acceso al directorio protegido. Los binarios están bajo Program Files; los datos solo se otorgan a SYSTEM, administradores y servicio. SQL Server recibe acceso a la carpeta de respaldos. No guardar claves o contraseñas en USB.

El técnico lee `primer-inicio.key` desde ProgramData, abre [Primer inicio](http://127.0.0.1:5080/primer-inicio), crea el administrador con una contraseña de al menos diez caracteres, mayúsculas, minúsculas y números, y conserva la contraseña en un lugar seguro. El código se elimina al terminar. Crear luego a la empleada con rol Vendedor. Cinco intentos erróneos bloquean el ingreso diez minutos.

La instalación todavía requiere abrir el navegador y completar la lista de VALIDACION.md, incluyendo la impresión y un reinicio real. El estado «Running» del servicio por sí solo no constituye aceptación.

## Configuración local

Ejemplo de `local.json`:

```json
{
  "Local": {
    "Port": 5080,
    "ConnectionString": "Server=.\\SQLEXPRESS;Database=CasaRosita;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;MultipleActiveResultSets=False",
    "BackupFolder": "C:\\ProgramData\\CasaRosita\\Backups",
    "BackupRetention": 14,
    "BackupHour": 20
  }
}
```

El certificado autofirmado del SQL local se acepta de forma explícita; la aplicación rechaza servidores remotos. Para cambiar la carpeta usar `Install.ps1 -PublishFolder ... -BackupFolder D:\RespaldosCasaRosita` para aplicar los permisos; la copia previa se realiza en la carpeta anterior antes del cambio. No continuar ante fallos del respaldo previo. Para cambiar puerto repetir con `-Port`; el acceso directo se actualiza. La retención admitida va de 2 a 365 copias; el horario, de 0 a 23. Reiniciar el servicio después de cambios manuales. Nunca borrar `Keys` durante una actualización normal.

## Actualizar por USB

1. Cerrar operaciones pendientes y dejar que ambas usuarias cierren la sesión.
2. Descargar una copia externa desde Configuración y conservarla. Llevar el nuevo paquete validado en USB.
3. Ejecutar nuevamente `Install.ps1` como administrador con la nueva carpeta publicada y la misma instancia.
4. El script detiene el servicio, crea y verifica un BACKUP previo, conserva los binarios anteriores, aplica migraciones y cambia la ruta del servicio.
5. Si falla el respaldo, la migración o la salud, **no abrir el negocio con esa actualización**. El script informa el error; un fallo de migración deja el servicio detenido. No arrancar versiones viejas contra un esquema nuevo sin verificar compatibilidad.
6. Comprobar versión, ingreso, productos, venta de prueba, comprobante, anulación, caja y último respaldo. Conservar el paquete y copia anteriores hasta cerrar la aceptación.

Si una versión falla después de migrar, restaurar explícitamente la copia previa con el técnico y volver a los binarios anteriores compatibles. No se aplica automáticamente una migración inversa destructiva. Ver RECUPERACION.md.

## Si no inicia

Revisar `Get-Service CasaRosita`, `Get-Service 'MSSQL$SQLEXPRESS'`, el registro de eventos de Windows y ProgramData\CasaRosita\Logs. Comprobar espacio libre, instancia, permisos de la cuenta virtual, configuración JSON y puerto ocupado. `/salud` responde 200 solo con conexión a SQL. No borrar la base ni reinstalar SQL para resolver un error de ingreso. No publicar en 0.0.0.0 ni habilitar acceso remoto.

No hay una consola abierta durante el funcionamiento normal. Para retirar el sistema, un técnico detiene y elimina el servicio; los datos, claves y respaldos se conservan por separado. No se incluye un desinstalador que borre datos del comercio.
