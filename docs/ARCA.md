# Factura C: certificado, habilitación y uso

## Qué incorpora el sistema

Emisión de factura C (código 11) para ventas locales de productos en pesos de un emisor monotributista. La venta se cobra primero; desde **Ventas → Ver detalle → Factura C / consultar estado** se pide la autorización fiscal. Se autentica con WSAA, solicita CAE a WSFEv1 y conserva número, fecha, receptor, emisor, importe, CAE y vencimiento. El documento se imprime en A4/80 mm o se guarda como PDF con el diálogo del navegador e incluye QR generado localmente.

Se requiere Internet para autorizar o recuperar una factura. El cobro, stock y caja siguen funcionando sin Internet. Un comprobante interno no reemplaza la factura fiscal.

Esta versión identifica a todos los compradores: nombre, domicilio y DNI para consumidor final, o CUIT con su condición de IVA. No aplica un umbral fijo para consumidor sin identificar. Admite consumidor final, responsable inscripto, monotributista y exento. El emisor debe ser monotributista y estar habilitado por ARCA; no se infiere su inscripción solamente a partir del CUIT.

No incluye servicios, exportaciones, moneda extranjera, factura de crédito MiPyME, notas de crédito/débito ni devoluciones de ventas facturadas. Una factura autorizada requiere nota de crédito para revertirse: la anulación interna queda bloqueada. Si se necesita revertirla, gestionar la nota fiscal por un canal habilitado y conciliar después la devolución local con el técnico; el sistema todavía no automatiza esa conciliación. No editar estados en SQL para eludir el bloqueo.

## 1. Preparar datos y entornos

Necesitás el CUIT del titular del monotributo, acceso a ARCA con clave fiscal y facultades para administrar sus servicios, razón social, domicilio comercial, identificación de Ingresos Brutos (o condición de exención real) e inicio de actividades. La contraseña de ARCA se usa exclusivamente en su portal, nunca se carga en Casa Rosita.

Hay dos ambientes independientes:

| Ambiente | Certificado y autorización | Uso |
| --- | --- | --- |
| Homologación | WSASS | Pruebas sin validez fiscal |
| Producción | Administración de Certificados Digitales + Administrador de Relaciones | Comprobantes fiscales reales |

Usá una instalación y base de pruebas separadas para homologación. Una venta facturada en pruebas no se vuelve a emitir automáticamente en producción. No intercambiar certificados ni restaurar una base de pruebas sobre la del local.

## 2. Crear clave privada y CSR

Un técnico puede realizar esta parte en la PC que custodiará la clave. Necesitás OpenSSL: verificá `openssl version`. La documentación oficial de ARCA enlaza opciones para Windows; descargalo desde una fuente del proyecto/proveedor que puedas verificar. OpenSSL solo hace falta para preparar el certificado, no para operar diariamente.

Creá una carpeta protegida **fuera de OneDrive, del repositorio y de la publicación**, por ejemplo `C:\ProgramData\CasaRosita\Fiscal\Homologacion`. La instalación del sistema ya protege ProgramData para administradores, SYSTEM y `NT SERVICE\CasaRosita`; verificá que una subcarpeta nueva herede esa protección. En una PC todavía no instalada, protegé primero la carpeta con un técnico.

Abrí PowerShell en esa carpeta y ejecutá, reemplazando `CUIT_REAL_SIN_GUIONES` por los 11 dígitos del titular y `NOMBRE_LEGAL` por su nombre o razón social:

```powershell
openssl genrsa -aes256 -out privada.key 2048
openssl req -new -sha256 -key privada.key -subj "/C=AR/O=NOMBRE_LEGAL/CN=CasaRosita/serialNumber=CUIT CUIT_REAL_SIN_GUIONES" -out solicitud.csr
```

El primer comando pide una contraseña para cifrar la clave; el segundo la pide para firmar la solicitud. El texto `CUIT ` con su espacio antes del número forma parte del campo `serialNumber`. El archivo que se carga a ARCA es **solicitud.csr**. La clave `privada.key` permanece bajo tu custodia. Guardá un respaldo cifrado de la clave y su contraseña por separado.

Para producción, repetí estos comandos en `C:\ProgramData\CasaRosita\Fiscal\Produccion`, con una clave nueva y los datos reales. No sobrescribas una clave de la que ya depende un certificado.

## 3. Obtener el certificado de homologación

1. Ingresá al portal de ARCA con clave fiscal.
2. Buscá/adherí **WSASS**, el autoservicio de acceso a APIs de homologación. Si no aparece, agregalo mediante los servicios de administración de relaciones disponibles para tu usuario.
3. En WSASS, creá el certificado de pruebas cargando el CSR. Indicá un alias identificable, por ejemplo `CasaRositaHomo`.
4. Descargá el certificado emitido, guardándolo como `certificado.crt` junto a la clave de homologación.
5. Dentro de WSASS, creá la autorización del certificado al servicio **wsfe / Facturación Electrónica** para el CUIT representado. Tener certificado sin autorización al servicio no alcanza.
6. Usá un punto de venta admitido por ARCA para ese CUIT en homologación. Confirmalo mediante las herramientas del ambiente o `FEParamGetPtosVenta`; no supongas que un número de producción está habilitado en pruebas.

Los rótulos del portal pueden variar. La referencia oficial de WSAA enlaza WSASS y explica la separación de ambientes.

## 4. Obtener y asociar el certificado de producción

1. Entrá a ARCA con clave fiscal y abrí **Administración de Certificados Digitales**. Si administrás a otra persona, seleccioná al titular correcto y verificá las delegaciones necesarias.
2. Elegí **Agregar alias**, por ejemplo `CasaRositaProduccion`, y cargá el CSR generado con la clave de producción.
3. Abrí el detalle del alias y descargá el certificado como `certificado.crt` en la carpeta de producción.
4. Abrí **Administrador de Relaciones de Clave Fiscal → Nueva relación**.
5. Buscá el servicio de negocio **Facturación Electrónica** correspondiente a **wsfe** entre los Web Services de ARCA.
6. Seleccioná como representante el computador fiscal/alias del certificado que acabás de crear y confirmá la relación. Verificá que corresponda al CUIT emisor.

## 5. Habilitar punto de venta de producción

En el servicio de administración de puntos de venta y domicilios del titular (ABM de puntos de venta), agregá un punto para **Factura Electrónica - Monotributo - Web Services**, con su domicilio asociado. Verificá el nombre vigente mostrado por ARCA.

Reservá ese punto para esta integración. El punto de Comprobantes en Línea corresponde a otro sistema de emisión; no reutilices su numeración ni un punto compartido por otro facturador. El sistema consulta el último número a ARCA y reserva el siguiente; no se carga manualmente un número inicial.

## 6. Preparar el PFX para Casa Rosita

En cada carpeta, usando la clave y certificado del mismo ambiente:

```powershell
openssl x509 -in certificado.crt -noout -subject -dates
openssl pkcs12 -export -inkey privada.key -in certificado.crt -out casarosita.pfx
```

OpenSSL pide la contraseña de la clave y una contraseña de exportación nueva para proteger el PFX. Anotá la fecha de vencimiento y programá su renovación antes de esa fecha. El PFX contiene certificado y clave privada; no se envía por correo ni se agrega a Git.

Guardá la contraseña de exportación en un archivo de texto UTF-8 `pfx-password.txt`, sin comillas ni líneas adicionales, en la misma carpeta protegida. El servicio necesita lectura de ambos archivos. Evitá escribir la contraseña en comandos que queden en el historial. Conservá además una copia cifrada externa del PFX.

Para renovar, prepará un nuevo CSR y agregá el certificado al alias correspondiente en ARCA. Descargalo, prepará el nuevo PFX y verificá su vigencia y autorización al servicio. Con el servicio Casa Rosita detenido, reemplazá el PFX y el archivo de contraseña de forma coordinada y reinicialo. Conservá un respaldo protegido anterior hasta verificar la renovación.

## 7. Configurar el sistema

Con el servicio detenido, agregá una sección `Fiscal` al mismo nivel que `Local` en `C:\ProgramData\CasaRosita\local.json`. **Conservá los valores existentes de Local**. Ejemplo incompleto: reemplazá CUIT, punto, fechas e Ingresos Brutos por los reales y dejá primero `Production: false` en la instalación de pruebas.

```json
"Fiscal": {
  "Enabled": true,
  "Production": false,
  "Cuit": "CUIT_REAL_SIN_GUIONES",
  "Point": 1,
  "CertificatePath": "C:\\ProgramData\\CasaRosita\\Fiscal\\Homologacion\\casarosita.pfx",
  "PasswordFile": "C:\\ProgramData\\CasaRosita\\Fiscal\\Homologacion\\pfx-password.txt",
  "GrossIncome": "INSCRIPCION_O_EXENCION_REAL",
  "ActivityStart": "2020-01-01"
}
```

Este fragmento se inserta dentro del objeto JSON existente, con la coma correspondiente entre `Local` y `Fiscal`. `Point` es el punto habilitado en ese ambiente. El sistema exige CUIT válido y coincidencia con el CUIT guardado en **Configuración → Datos del negocio**, donde también debés completar razón social y domicilio.

Reiniciá el servicio. El usuario del servicio debe poder leer el PFX y el archivo de contraseña, y escribir el caché protegido del ticket WSAA en la carpeta de datos. No necesita acceso a la clave fiscal de ARCA. El reloj y zona horaria de Windows deben estar correctos.

## 8. Probar y pasar a producción

1. En la instalación de pruebas, confirmá una venta y abrí su pantalla **Factura C**. Debe mostrar **HOMOLOGACIÓN**.
2. Completá datos del receptor y emití. Verificá CAE, vencimiento, total, datos del titular, datos del receptor y QR. La impresión debe decir **SIN VALIDEZ FISCAL**.
3. Reabrí esa venta y comprobá que conserva el mismo número y CAE. Imprimí en el papel usado por el local; probá también Guardar como PDF.
4. En la instalación real, configurá el certificado y punto de producción, `Production: true` y los datos fiscales del titular. No copies ventas de pruebas a la base real.
5. Emití la primera factura de producción sobre una operación real, verificá su autorización en ARCA y la impresión. Registrar una factura de producción tiene efectos fiscales; no emitir ventas ficticias para probar.

No se ha realizado una autorización real contra ARCA como parte de la implementación: requiere tus certificados y habilitaciones. La validación automatizada usa respuestas SOAP simuladas y SQL Server real.

## Si se corta Internet o ARCA no responde

La venta permanece cobrada. La factura queda `PendienteARCA`; no la vuelvas a cobrar. Entrá a su pantalla y elegí **Recuperar factura pendiente**. El sistema consulta tipo 11, punto y número persistidos. Si encuentra un CAE, verifica que coincida con la venta y lo guarda. Solo ante respuesta explícita de que no existe y secuencia compatible vuelve a enviar la misma solicitud. Mientras haya una pendiente se bloquean otras emisiones.

Un rechazo explícito queda `RechazadaARCA`, con observaciones para corregir y reintentar. Un timeout, error de autenticación o respuesta incompleta no se toma como rechazo definitivo. Si cambiaste CUIT, ambiente o punto, restaurá esa configuración para recuperar la solicitud. Una diferencia de numeración o datos requiere conciliación técnica. No emitir por otro canal una venta de resultado incierto.

Tras restaurar un backup en producción, conciliá con ARCA cualquier factura emitida después de esa copia antes de volver a facturar: el respaldo local no revierte autorizaciones fiscales. Guardá también los certificados, contraseñas y configuración mediante un respaldo protegido separado del backup SQL. El caché de WSAA está cifrado con las claves de protección de la instalación; un cambio de PC puede requerir renovarlo.

## Fuentes oficiales

- [Monotributo: comprobantes C y exportación E](https://www.arca.gob.ar/facturacion/monotributo/comprobantes.asp).
- [WSAA: certificados y autorización por ambiente](https://www.arca.gob.ar/ws/documentacion/wsaa.asp).
- [Generación del certificado de producción y CSR](https://arca.gob.ar/ws/WSAA/WSAA.ObtenerCertificado.pdf).
- [Asociación del certificado a un Web Service](https://www.arca.gob.ar/ws/WSAA/wsaa_asociar_certificado_a_wsn_produccion.pdf).
- [Guía de habilitación de comprobantes/puntos de venta](https://serviciosweb.arca.gob.ar/genericos/guiasPasoPaso/VerGuia.aspx?id=163).
- [Documentación WSFEv1](https://www.arca.gob.ar/ws/documentacion/ws-factura-electronica.asp).
- [Especificación del QR](https://arca.gob.ar/fe/qr/documentos/QRespecificaciones.pdf).

La URL del manual WSFEv1 consultada durante el desarrollo muestra una revisión fechada 1 de diciembre de 2026, posterior al inicio de este trabajo. No se presupone que cambios futuros ya estén vigentes: antes de habilitar producción, validar los contratos y catálogos del ambiente de ARCA y completar la prueba con certificado propio.
