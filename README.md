# Casa Rosita

Punto de venta local para Windows, .NET 10 LTS, Blazor Interactive Server, EF Core y SQL Server Express. Versión inicial 0.1.0. Las ventas, stock y caja funcionan sin Internet; la autorización fiscal de factura C necesita conexión a ARCA. Los paquetes de desarrollo se descargan al compilar; la publicación incluye runtime, Bootstrap, CSS, JavaScript e iconografía Unicode locales.

**Estado:** implementación funcional con pruebas SQL reales. No habilitar para producción hasta completar la aceptación de instalación, reinicio, navegador e impresoras en la PC del local. Ver [VALIDACION.md](docs/VALIDACION.md).

## Arquitectura

Un monolito en `src/CasaRosita`: componentes Blazor, servicios de aplicación y persistencia. El servidor Kestrel escucha exclusivamente en `127.0.0.1`, puerto 5080 configurable. SQL Express pertenece a la misma PC; no se usan Docker, IIS ni nube. Integración opcional con WSAA y WSFEv1 de ARCA. Una única caja lógica compartida.

Identity mantiene usuarios, hash de contraseñas, roles, bloqueo por intentos y cookie local con antifalsificación. Los servicios vuelven a consultar que el usuario esté activo y tenga autorización, incluso si conserva una sesión Blazor abierta. El primer administrador requiere un código aleatorio guardado en una carpeta protegida.

`Commerce` confirma cabecera, detalle histórico, pago, stock, inventario y caja dentro de una transacción serializable. Un bloqueo de aplicación SQL coordina las operaciones de esta única caja. Un índice único por operación evita duplicar cobros. Los precios se releen y comparan con los mostrados antes de aceptar el cobro. Los ajustes usan auditoría y `rowversion` protege la edición del catálogo. Las ventas se anulan conservando su historial; las devoluciones se registran en la caja actualmente abierta, sin alterar cierres anteriores.

El navegador conserva solo la clave del cobro pendiente. Tras una desconexión consulta el resultado bajo el mismo bloqueo SQL. Nunca se informa éxito antes de completar el commit. Los artículos sin confirmar no son ventas y un reinicio puede perder ese carrito; no se debe borrar el almacenamiento del navegador durante un cobro incierto.

Los importes y cantidades son `decimal`; SQL usa `decimal(18,3)`. Los importes comerciales se redondean a dos decimales, por renglón, alejando el punto medio de cero. Unidades enteras y metros con hasta tres decimales. El descuento se expresa como importe total y requiere administrador.

## Funcionalidad incluida

- Inicio con ventas del día, caja y productos bajo mínimo.
- ABM de productos con baja lógica, categorías iniciales, talle/color simples, costos opcionales y movimientos de stock.
- Mostrador con búsqueda y código de barras, cantidades decimales, cinco medios de pago manuales y cliente opcional.
- Ventas por número/fechas, detalle histórico, anulación autorizada y comprobantes internos A4/80 mm.
- Apertura, ingresos/egresos de efectivo, cierre con diferencia e historial de cajas.
- Clientes opcionales sin cuenta corriente; gestión de usuarios y datos del negocio/logo.
- Informes por período, categoría, producto y medio de pago; exportación CSV de ventas, caja y stock, con protección contra fórmulas.
- BACKUP SQL con CHECKSUM y VERIFYONLY, respaldo diario al estar encendida la PC, retención y descarga a pendrive.
- Publicación self-contained win-x64, scripts de instalación/actualización/restauración y propuesta Inno Setup.
- Factura C para productos en pesos: certificado PFX, WSAA, CAE por WSFEv1, recuperación de solicitudes inciertas y factura imprimible con QR. Deshabilitada inicialmente. Configuración y certificado: [ARCA.md](docs/ARCA.md).

## Compilar y probar

En la PC de desarrollo: Windows x64, SDK .NET 10.0.401 o parche compatible e instancia SQL Server Express `SQLEXPRESS`. La identidad de desarrollo necesita crear/eliminar bases de prueba. No usar una cuenta así en el navegador del comercio.

```powershell
dotnet restore tests/CasaRosita.Checks
dotnet tool restore
dotnet build tests/CasaRosita.Checks --no-restore
.\scripts\Test.ps1
.\scripts\Publish.ps1
.\scripts\Smoke.ps1
```

`Test.ps1` genera una base `CasaRosita_Test_<GUID>` y la elimina al terminar. Las pruebas no usan SQLite ni un proveedor en memoria. `Smoke.ps1` usa exclusivamente `CasaRosita_Validation`, puerto 5089 y `.local/validation`; verifica el ejecutable autónomo, primer usuario, login, pantallas y antifalsificación. Esa base de validación permanece para inspección. Sus credenciales temporales nunca se usan en producción. La opción `-KeepRunning` deja el proceso de validación oculto; el PID se guarda en `.local/validation/app.pid`.

La publicación queda en `artifacts/publish`. El runtime de distribución se fija en 10.0.13 y la dependencia Pkcs en 10.0.13, parche de los avisos de seguridad de octubre de 2026. Antes de distribuir futuras versiones, revisar nuevamente paquetes y runtime.

## Instalar, actualizar y recuperar

Ver [INSTALACION.md](docs/INSTALACION.md), [RECUPERACION.md](docs/RECUPERACION.md) y [VALIDACION.md](docs/VALIDACION.md). No copiar MDF/LDF mientras el motor está ejecutándose. Los datos y configuraciones no deben guardarse dentro de la publicación ni en una carpeta sincronizada como OneDrive para la instalación del comercio.

## Límites de esta versión

Se registra un único medio de pago por venta; Mercado Pago y tarjetas se registran manualmente, sin integraciones ni comprobación de acreditación. No hay cuentas corrientes, pagos combinados ni devolución parcial. La integración fiscal incluye factura C de productos en pesos con receptor identificado; no incluye servicios, exportaciones ni notas de crédito/débito. Las ventas con factura autorizada o incierta no admiten anulación interna. Las categorías iniciales son fijas. La restauración está reservada al técnico administrador y fuera de la interfaz diaria.

La carpeta, retención, horario y puerto se configuran en `local.json` mediante la instalación; necesitan reiniciar el servicio. Los datos comerciales y el papel se ajustan desde Configuración. Las fechas comerciales siguen el reloj y zona horaria de Windows, que deben configurarse como Argentina. El servicio debe estar encendido para efectuar copias diarias; al reiniciar se recuperan días atrasados. Una PC apagada no puede hacer respaldos. Se recomienda UPS y copia externa diaria. Ningún software garantiza recuperación ante daño físico del disco.
