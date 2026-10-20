# Respaldos y recuperación

La única PC concentra la información. Un archivo en su mismo disco no alcanza: al cerrar cada día, generar/verificar un respaldo, descargarlo desde Configuración a un pendrive y desconectar el pendrive. Rotar al menos dos medios y guardar uno fuera del local. Configurar también una UPS y probar su apagado seguro.

El servicio realiza `BACKUP DATABASE ... WITH COPY_ONLY, CHECKSUM` y `RESTORE VERIFYONLY ... WITH CHECKSUM`. La copia es consistente aunque se estén registrando ventas. No copiar MDF/LDF del motor en ejecución. La retención elimina solo archivos propios de copias exitosas en la carpeta configurada. Las copias previas a actualización/restauración se conservan para intervención del técnico.

El horario diario se evalúa cada minuto mientras la aplicación está ejecutándose; un fallo reintenta una hora después. Al reiniciar se recupera un atraso de días. Configuración informa fecha y resultado de intentos. Un respaldo fallido requiere atender la carpeta, permisos del **servicio SQL**, disco lleno o disponibilidad del motor. Si SQL está caído y no puede registrarse el fallo en la base, el diagnóstico queda en los archivos de registro.

## Restaurar con un técnico

1. Detener el trabajo y preservar el disco/archivos actuales. No seguir vendiendo sobre una restauración incompleta.
2. Elegir una copia externa con fecha y versión conocidas. Copiarla a una carpeta accesible para el servicio SQL. Guardar también el paquete del programa y `local.json` en el plan de recuperación; las contraseñas de usuarios se incluyen en la base.
3. En Windows nuevo, instalar previamente SQL Express desde USB y los binarios de la versión correspondiente. Crear/configurar el servicio mediante la instalación. No mezclar una base de una versión nueva con binarios viejos.
4. Desde PowerShell como administrador y una identidad SQL con permisos de restauración:

```powershell
.\scripts\Restore.ps1 -BackupFile 'D:\Recuperacion\CasaRosita-20261018.bak' -Confirmation 'RESTAURAR CASA ROSITA'
```

El script verifica CHECKSUM y cabecera, solicita confirmación de sobrescritura, detiene el servicio y guarda una copia consistente del estado existente antes de reemplazar. Usa rutas de datos del motor para reubicar los archivos y ejecuta `DBCC CHECKDB`. Solo admite la base `CasaRosita`; no es una herramienta de restauración general. Si falla, deja el servicio detenido y conserva las copias. El motor SQL debe tener acceso de lectura al respaldo. Mantener la carpeta de recuperación protegida.

5. Ejecutar `Install.ps1` con la versión elegida para reaplicar permisos de la identidad virtual y migrar de forma controlada si corresponde. En una recuperación de versión anterior, usar exclusivamente los binarios compatibles con esa copia.
6. Abrir el sistema, iniciar sesión, revisar últimas ventas, productos, stock y caja. Confirmar que los totales concuerdan con la fecha de la copia. Registrar por escrito hasta qué operación se recuperó; las ventas posteriores al respaldo pueden requerir reconstrucción documental.
7. Crear un nuevo respaldo, verificarlo y copiarlo a un medio externo antes de retomar.

Si se conservaron las claves de sesión en la misma PC, pueden reutilizarse. En una PC nueva pueden generarse nuevas claves y las usuarias deberán iniciar sesión nuevamente. El backup SQL no incorpora `local.json`, archivos de diagnóstico ni claves de protección: conservar una copia administrativa separada y protegida de la configuración.

## Probar periódicamente

Al menos una vez por mes y después de una actualización importante, restaurar una copia externa en una instancia o PC de prueba aislada, con otro nombre de base y puerto; no apuntar nunca al servicio productivo. Ejecutar VERIFYONLY, restauración real, CHECKDB, ingreso y consulta de ventas. Documentar fecha, versión, cantidad de ventas y resultado. `VERIFYONLY` no sustituye la prueba real de restauración ni verifica por sí solo toda la integridad lógica de los datos.

Las transacciones del motor revierten operaciones que no llegaron al commit después de un corte. Si el navegador perdió la respuesta, Nueva venta consulta su clave de operación antes de repetir el cobro. Si falta esa clave, revisar Ventas antes de cobrar otra vez. No se promete recuperar datos de un disco físicamente dañado.
