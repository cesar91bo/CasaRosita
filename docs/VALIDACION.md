# Validación de la versión 0.1.0

Fecha: 18 de octubre de 2026, Argentina. Estas pruebas no sustituyen la aceptación en el equipo final del comercio.

## Ejecutado en esta computadora Windows

| Verificación | Resultado |
| --- | --- |
| Compilación de aplicación y comprobaciones .NET 10 | Correcta, sin errores ni advertencias |
| Migraciones EF Core sobre SQL Express real | Correctas |
| Motor usado | SQL Server Express 2022, versión 16.0.1000.6; actualizar antes de producción |
| Venta de 1,75 m × $8.000 | $14.000 persistidos |
| Reintentos y confirmaciones simultáneas | Una única venta y pago por operación |
| Dos ventas sobre la última unidad | Solo una confirma |
| Stock insuficiente, cambio de precio y venta sin caja | Rechazados sin cabeceras parciales |
| Permisos de descuento, stock y anulación | Comprobados |
| Cuenta deshabilitada con circuito abierto | Operación rechazada |
| Cierre y separación efectivo / electrónicos | Comprobados |
| Anulación después del cierre original | Registra devolución en caja actual y conserva cierre anterior |
| BACKUP y VERIFYONLY con CHECKSUM | Correctos |
| Restauración real en una segunda base temporal y CHECKDB | Correctos; ventas, pagos, anulaciones y stock recuperados |
| Publicación win-x64 self-contained | Generada con runtime 10.0.13 |
| Arranque del ejecutable autónomo y salud SQL | Correctos en 127.0.0.1:5089 |
| Primer administrador con clave de instalación | Creado mediante formulario real |
| Autenticación, CSV y antifalsificación HTTP | Correctos |
| Renderizado autenticado de ocho pantallas | Correcto |
| Navegador con interacción Blazor real | Alta de tela, ajuste de stock, apertura, venta y comprobante comprobados |
| Venta desde navegador | N.º 1 en CasaRosita_Validation; 1,75 metros, $14.000 |
| Cierre desde navegador | $15.000 esperado y contado, diferencia cero |

Las 32 comprobaciones automatizadas aprobaron. Son ejecutables mediante `scripts/Test.ps1`: generan bases aleatorias `CasaRosita_Test_*` y las eliminan. Fallan si no existe conexión SQL. No reemplazan el motor por SQLite/memoria. El script HTTP `scripts/Smoke.ps1` conserva la base `CasaRosita_Validation` para inspección y usa credenciales temporales en `.local`, excluidas de Git. La consulta final de vulnerabilidades NuGet, incluidas dependencias transitivas, no informó paquetes vulnerables.

La primera prueba del ejecutable encontró y corrigió el parsing del argumento `--migrate`: se retira ese indicador antes de pasar los argumentos de configuración al host. Se eliminó después la base vacía generada por ese intento, verificando su hora de creación y ausencia de usuarios, productos y ventas. Las comprobaciones posteriores usan la base aislada correcta. La revisión interactiva detectó y corrigió la actualización de campos y dos formatos monetarios que no se advertían solo al compilar.

## Pendiente antes de declarar apto para el local

- Ejecutar Install.ps1 con elevación real de Windows en una VM limpia y en el equipo final. El entorno actual no tiene un token de administrador Windows; no se instaló un servicio permanente ni se modificó el escritorio del usuario.
- Comprobar identidad virtual, ACL, permisos mínimos SQL, dependencia y recuperación del servicio. Probar respaldo bajo esa identidad, no solo bajo la cuenta del técnico.
- Compilar y probar el asistente Inno Setup; verificar error por SQL ausente y actualización sobre instalación existente.
- Probar el script de restauración administrativo completo sobre una instalación de ensayo. El test automatizado ya restaura con el motor, pero no instala ni detiene el servicio Windows real.
- Instalar/abrir con Internet desconectado y comprobar que no hay solicitudes a servicios externos.
- Imprimir físicamente A4 y térmica 80 mm, incluyendo corte de renglones, márgenes y logo. El botón abre la impresión del navegador; no hay envío directo silencioso.
- Reiniciar Windows y verificar arranque del servicio, acceso directo, ingreso y persistencia de ventas/caja.
- Cortar y recuperar el proceso durante un cobro en una base de ensayo. Verificar la recuperación por clave en el navegador; probar también pérdida de respuesta después del commit.
- Probar un corte eléctrico controlado solo en un entorno descartable y evaluar UPS. No realizar esta prueba sobre datos reales.
- Restaurar desde un pendrive en otra PC y ejecutar CHECKDB, ingreso y revisión de totales.
- Hacer el recorrido con la dueña y la empleada a 1366 × 768. Confirmar que leen cantidades e importes, entienden mensajes y no necesitan asistencia.

## Recorrido diario de aceptación

1. Encender Windows y esperar inicio automático del motor y Casa Rosita.
2. Doble clic en el acceso directo del escritorio e iniciar sesión como Vendedor.
3. Abrir caja con saldo inicial.
4. Buscar tela por código y vender 1,75 m; agregar un producto por unidad.
5. Cobrar y confirmar. Guardar número de venta. Reabrir la página y comprobar que no duplica el cobro.
6. Imprimir comprobante interno y verificar su leyenda no fiscal.
7. Consultar ventas e historial de inventario. Intentar una acción de administrador como Vendedor y confirmar rechazo.
8. Cerrar caja contando solo efectivo. Revisar diferencia y pagos electrónicos.
9. Crear respaldo manual, descargarlo a pendrive, comprobar archivo y retirarlo.
10. Apagar y encender Windows; comprobar que ventas, caja e inventario permanecen y que el servicio inicia sin consola.

Registrar versión, fecha, impresoras, responsables y evidencia del recorrido. Hasta entonces, esta entrega es una versión para validación técnica y de uso, no una instalación productiva aceptada.

## Ampliación: factura C (20/10/2026)

- Compilación de aplicación y pruebas correcta. Publicación Windows x64 self-contained generada.
- 62 comprobaciones aprobadas con SQL Server Express real, incluyendo solicitudes fiscales simultáneas, CAE con respuesta perdida, recuperación sin duplicar emisión, rechazo explícito, reenvío del mismo número tras ausencia confirmada, secuencia externa incompatible y bloqueo de anulación fiscal.
- Pruebas SOAP con transporte simulado: serialización independiente de cultura argentina, importes C sin IVA discriminado, código 11, consulta 602, recuperación de CAE, diferencias de importe y fallo HTTP. No se enviaron comprobantes a ARCA.
- Renderizado del componente de factura autorizado: código, CAE, QR PNG local, emisor histórico y marca visible de homologación. Falta inspección de impresión física A4/80 mm.
- Backup y restauración SQL verificados también para facturas y CAE, además de ventas, pagos, anulaciones y stock.
- Recorrido HTTP del ejecutable publicado aprobado en `CasaRosita_Validation_FacturaC20261020`: primer administrador, autenticación, ocho pantallas, CSV y rechazo de POST sin antifalsificación. Proceso detenido al finalizar. La base de ensayo permanece para inspección.
- El recorrido sobre la base de validación anterior inició el ejecutable pero no pudo autenticar con la credencial disponible; se usó la base nueva para completar la comprobación sin cambiar usuarios previos. `Smoke.ps1 -Database CasaRosita_Validation_<Sufijo>` permite aislar futuras verificaciones.
- No se modificó el esquema SQL: se utilizan los campos fiscales existentes de Sale. El JSON fiscal guarda la solicitud histórica y permite recuperar resultados inciertos.

Pendiente: obtener certificado de homologación, autorizar wsfe, emitir y recuperar un comprobante de pruebas ante ARCA, verificar impresión y habilitar por separado producción con el certificado/punto del titular. Guía: [ARCA.md](ARCA.md). No se habilitó facturación productiva ni se generaron claves/certificados del usuario. Las notas de crédito y la conciliación de devoluciones fiscales requieren otra ampliación; la anulación interna de facturas autorizadas o pendientes se bloquea.
