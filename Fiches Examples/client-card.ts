import * as XLSX from "xlsx-js-style";
import { Client, ClientSale, ClientInstallment } from "@/types/client";
import { format, parseISO } from "date-fns";

export function exportClientCard(
  client: Client,
  sales: ClientSale[],
  installments: ClientInstallment[]
) {
  // Helper function to create a styled cell
  const createStyledCell = (value: any, style: any) => ({
    v: value,
    t: typeof value === 'number' ? 'n' : 's',
    s: style
  });

  // Common styles
  const styles = {
    title: {
      font: { name: "Arial", sz: 14, bold: true },
      alignment: { vertical: "center", horizontal: "center" },
      fill: { patternType: "solid", fgColor: { rgb: "E0E0E0" } }
    },
    header: {
      font: { name: "Arial", sz: 11, bold: true },
      alignment: { vertical: "center", horizontal: "left" }
    },
    headerValue: {
      font: { name: "Arial", sz: 11 },
      alignment: { vertical: "center", horizontal: "left" }
    },
    tableHeader: {
      font: { name: "Arial", sz: 11, bold: true },
      alignment: { vertical: "center", horizontal: "center" },
      fill: { patternType: "solid", fgColor: { rgb: "F0F0F0" } },
      border: {
        top: { style: "medium", color: { rgb: "000000" } },
        bottom: { style: "medium", color: { rgb: "000000" } },
        left: { style: "medium", color: { rgb: "000000" } },
        right: { style: "medium", color: { rgb: "000000" } }
      }
    },
    tableCell: {
      font: { name: "Arial", sz: 11 },
      alignment: { vertical: "center", horizontal: "center" },
      border: {
        top: { style: "thin", color: { rgb: "000000" } },
        bottom: { style: "thin", color: { rgb: "000000" } },
        left: { style: "thin", color: { rgb: "000000" } },
        right: { style: "thin", color: { rgb: "000000" } }
      }
    }
  };

  // Initialize worksheet
  const ws: any = {};
  const wb = XLSX.utils.book_new();

  // Set column widths
  ws['!cols'] = [
    { wch: 35 }, // A
    { wch: 20 }, // B
    { wch: 20 }, // C
    { wch: 20 }, // D
    { wch: 20 }  // E
  ];

  // Title
  ws['A1'] = createStyledCell("Fiche de Client / Client Sheet", styles.title);

  // Client Information
  ws['A3'] = createStyledCell("Date :", styles.header);
  ws['B3'] = createStyledCell(format(new Date(), "dd/MM/yyyy"), styles.headerValue);
  ws['D3'] = createStyledCell("N°CCP:", styles.header);
  ws['E3'] = createStyledCell(client.postal_account_number, styles.headerValue);

  ws['A4'] = createStyledCell("Nom:", styles.header);
  ws['B4'] = createStyledCell(client.name, styles.headerValue);
  ws['D4'] = createStyledCell("nombre d'écheances:", styles.header);
  ws['E4'] = createStyledCell(installments.length, styles.headerValue);

  ws['A5'] = createStyledCell("Prenom:", styles.header);
  ws['B5'] = createStyledCell(client.firstname, styles.headerValue);
  ws['D5'] = createStyledCell("Montant écheances:", styles.header);
  ws['E5'] = createStyledCell(installments[0]?.amount || '', styles.headerValue);

  ws['A6'] = createStyledCell("Telephone:", styles.header);
  ws['B6'] = createStyledCell(client.phone, styles.headerValue);

  // Add the 'clientFile' field to the exported client card
  ws['A7'] = createStyledCell("Client File:", styles.header); // Add label for 'clientFile'
  ws['B7'] = createStyledCell(client.clientFile, styles.headerValue); // Add value for 'clientFile'

  // Calculate the earliest start_date and latest end_date from the sales array
  const startDates = sales.map((sale) => parseISO(sale.start_date));
  const endDates = sales.map((sale) => parseISO(sale.end_date));
  const earliestStartDate = format(new Date(Math.min(...startDates.map(date => date.getTime()))), "dd/MM/yyyy");
  const latestEndDate = format(new Date(Math.max(...endDates.map(date => date.getTime()))), "dd/MM/yyyy");

  // Add the 'start_date' and 'end_date' fields to the exported client card
  ws['A8'] = createStyledCell("Start Date:", styles.header); // Add label for 'start_date'
  ws['B8'] = createStyledCell(earliestStartDate, styles.headerValue); // Add value for 'start_date'
  ws['D8'] = createStyledCell("End Date:", styles.header); // Add label for 'end_date'
  ws['E8'] = createStyledCell(latestEndDate, styles.headerValue); // Add value for 'end_date'

  // Products Table
  const PRODUCTS_START_ROW = 10; // Adjusted row index to accommodate the new fields
  ws['A10'] = createStyledCell("Article", styles.tableHeader);
  ws['B10'] = createStyledCell("Quantité", styles.tableHeader);
  ws['C10'] = createStyledCell("PU", styles.tableHeader);

  // Fill products
  sales.forEach((sale, index) => {
    const rowIndex = PRODUCTS_START_ROW + 1 + index;
    ws[`A${rowIndex}`] = createStyledCell(sale.product_name, styles.tableCell);
    ws[`B${rowIndex}`] = createStyledCell(sale.quantity, styles.tableCell);
    ws[`C${rowIndex}`] = createStyledCell(sale.total_amount, styles.tableCell);
  });

  // Calculate total row position
  const TOTAL_ROW = PRODUCTS_START_ROW + sales.length + 1;

  // Total row
  ws[`A${TOTAL_ROW}`] = createStyledCell("TOTAL", { ...styles.tableCell, font: { ...styles.tableCell.font, bold: true } });
  ws[`B${TOTAL_ROW}`] = createStyledCell('', styles.tableCell);
  ws[`C${TOTAL_ROW}`] = createStyledCell(
    sales.reduce((sum, sale) => sum + (sale.total_amount || 0), 0),
    { ...styles.tableCell, font: { ...styles.tableCell.font, bold: true } }
  );

  // Installments Table
  const INSTALLMENTS_START_ROW = TOTAL_ROW + 2;
  ws[`A${INSTALLMENTS_START_ROW}`] = createStyledCell("Date de payment", styles.tableHeader);
  ws[`B${INSTALLMENTS_START_ROW}`] = createStyledCell("Montant", styles.tableHeader);
  ws[`C${INSTALLMENTS_START_ROW}`] = createStyledCell("Statut", styles.tableHeader);

  // Fill installments
  installments.forEach((installment, index) => {
    const rowIndex = INSTALLMENTS_START_ROW + 1 + index;
    ws[`A${rowIndex}`] = createStyledCell(format(new Date(installment.due_date), "PP"), styles.tableCell);
    ws[`B${rowIndex}`] = createStyledCell(installment.amount, styles.tableCell);
    ws[`C${rowIndex}`] = createStyledCell(installment.status, styles.tableCell);
  });

  // Calculate last row for signatures
  const SIGNATURES_ROW = INSTALLMENTS_START_ROW + installments.length + 4;
  ws[`A${SIGNATURES_ROW}`] = createStyledCell("Signature gerant", styles.header);
  ws[`C${SIGNATURES_ROW}`] = createStyledCell("Signature client", styles.header);

  // Set worksheet range dynamically
  ws['!ref'] = `A1:E${SIGNATURES_ROW + 1}`;

  // Merge cells
  ws['!merges'] = [
    { s: { r: 0, c: 0 }, e: { r: 0, c: 4 } }, // Title
  ];

  // Add worksheet to workbook
  XLSX.utils.book_append_sheet(wb, ws, "Client Card");

  // Save file
  XLSX.writeFile(wb, `Fiche-client-${client.name}.xlsx`);
}


// import * as XLSX from "xlsx-js-style";
// import { Client, ClientSale, ClientInstallment } from "@/types/client";
// import { format } from "date-fns";

// export function exportClientCard(
//   client: Client,
//   sales: ClientSale[],
//   installments: ClientInstallment[]
// ) {
//   // Helper function to create a styled cell
//   const createStyledCell = (value: any, style: any) => ({
//     v: value,
//     t: typeof value === 'number' ? 'n' : 's',
//     s: style
//   });

//   // Common styles
//   const styles = {
//     title: {
//       font: { name: "Arial", sz: 14, bold: true },
//       alignment: { vertical: "center", horizontal: "center" },
//       fill: { patternType: "solid", fgColor: { rgb: "E0E0E0" } }
//     },
//     header: {
//       font: { name: "Arial", sz: 11, bold: true },
//       alignment: { vertical: "center", horizontal: "left" }
//     },
//     headerValue: {
//       font: { name: "Arial", sz: 11 },
//       alignment: { vertical: "center", horizontal: "left" }
//     },
//     tableHeader: {
//       font: { name: "Arial", sz: 11, bold: true },
//       alignment: { vertical: "center", horizontal: "center" },
//       fill: { patternType: "solid", fgColor: { rgb: "F0F0F0" } },
//       border: {
//         top: { style: "medium", color: { rgb: "000000" } },
//         bottom: { style: "medium", color: { rgb: "000000" } },
//         left: { style: "medium", color: { rgb: "000000" } },
//         right: { style: "medium", color: { rgb: "000000" } }
//       }
//     },
//     tableCell: {
//       font: { name: "Arial", sz: 11 },
//       alignment: { vertical: "center", horizontal: "center" },
//       border: {
//         top: { style: "thin", color: { rgb: "000000" } },
//         bottom: { style: "thin", color: { rgb: "000000" } },
//         left: { style: "thin", color: { rgb: "000000" } },
//         right: { style: "thin", color: { rgb: "000000" } }
//       }
//     }
//   };

//   // Initialize worksheet
//   const ws: any = {};
//   const wb = XLSX.utils.book_new();

//   // Set column widths
//   ws['!cols'] = [
//     { wch: 35 }, // A
//     { wch: 20 }, // B
//     { wch: 20 }, // C
//     { wch: 20 }, // D
//     { wch: 20 }  // E
//   ];

//   // Title
//   ws['A1'] = createStyledCell("Fiche de Client / Client Sheet", styles.title);

//   // Client Information
//   ws['A3'] = createStyledCell("Date :", styles.header);
//   ws['B3'] = createStyledCell(format(new Date(), "dd/MM/yyyy"), styles.headerValue);
//   ws['D3'] = createStyledCell("N°CCP:", styles.header);
//   ws['E3'] = createStyledCell(client.postal_account_number, styles.headerValue);

//   ws['A4'] = createStyledCell("Nom:", styles.header);
//   ws['B4'] = createStyledCell(client.name, styles.headerValue);
//   ws['D4'] = createStyledCell("nombre d'écheances:", styles.header);
//   ws['E4'] = createStyledCell(installments.length, styles.headerValue);

//   ws['A5'] = createStyledCell("Prenom:", styles.header);
//   ws['B5'] = createStyledCell(client.firstname, styles.headerValue);
//   ws['D5'] = createStyledCell("Montant écheances:", styles.header);
//   ws['E5'] = createStyledCell(installments[0]?.amount || '', styles.headerValue);

//   ws['A6'] = createStyledCell("Telephone:", styles.header);
//   ws['B6'] = createStyledCell(client.phone, styles.headerValue);

//   // Add the 'clientFile' field to the exported client card
//   ws['A7'] = createStyledCell("Client File:", styles.header); // Add label for 'clientFile'
//   ws['B7'] = createStyledCell(client.clientFile, styles.headerValue); // Add value for 'clientFile'

//   // Products Table
//   const PRODUCTS_START_ROW = 9; // Adjusted row index to accommodate the new 'clientFile' field
//   ws['A9'] = createStyledCell("Article", styles.tableHeader);
//   ws['B9'] = createStyledCell("Quantité", styles.tableHeader);
//   ws['C9'] = createStyledCell("PU", styles.tableHeader);

//   // Fill products
//   sales.forEach((sale, index) => {
//     const rowIndex = PRODUCTS_START_ROW + 1 + index;
//     ws[`A${rowIndex}`] = createStyledCell(sale.product_name, styles.tableCell);
//     ws[`B${rowIndex}`] = createStyledCell(sale.quantity, styles.tableCell);
//     ws[`C${rowIndex}`] = createStyledCell(sale.total_amount, styles.tableCell);
//   });

//   // Calculate total row position
//   const TOTAL_ROW = PRODUCTS_START_ROW + sales.length + 1;

//   // Total row
//   ws[`A${TOTAL_ROW}`] = createStyledCell("TOTAL", { ...styles.tableCell, font: { ...styles.tableCell.font, bold: true } });
//   ws[`B${TOTAL_ROW}`] = createStyledCell('', styles.tableCell);
//   ws[`C${TOTAL_ROW}`] = createStyledCell(
//     sales.reduce((sum, sale) => sum + (sale.total_amount || 0), 0),
//     { ...styles.tableCell, font: { ...styles.tableCell.font, bold: true } }
//   );

//   // Installments Table
//   const INSTALLMENTS_START_ROW = TOTAL_ROW + 2;
//   ws[`A${INSTALLMENTS_START_ROW}`] = createStyledCell("Date de payment", styles.tableHeader);
//   ws[`B${INSTALLMENTS_START_ROW}`] = createStyledCell("Montant", styles.tableHeader);
//   ws[`C${INSTALLMENTS_START_ROW}`] = createStyledCell("Statut", styles.tableHeader);

//   // Fill installments
//   installments.forEach((installment, index) => {
//     const rowIndex = INSTALLMENTS_START_ROW + 1 + index;
//     ws[`A${rowIndex}`] = createStyledCell(format(new Date(installment.due_date), "PP"), styles.tableCell);
//     ws[`B${rowIndex}`] = createStyledCell(installment.amount, styles.tableCell);
//     ws[`C${rowIndex}`] = createStyledCell(installment.status, styles.tableCell);
//   });

//   // Calculate last row for signatures
//   const SIGNATURES_ROW = INSTALLMENTS_START_ROW + installments.length + 4;
//   ws[`A${SIGNATURES_ROW}`] = createStyledCell("Signature gerant", styles.header);
//   ws[`C${SIGNATURES_ROW}`] = createStyledCell("Signature client", styles.header);

//   // Set worksheet range dynamically
//   ws['!ref'] = `A1:E${SIGNATURES_ROW + 1}`;

//   // Merge cells
//   ws['!merges'] = [
//     { s: { r: 0, c: 0 }, e: { r: 0, c: 4 } }, // Title
//   ];

//   // Add worksheet to workbook
//   XLSX.utils.book_append_sheet(wb, ws, "Client Card");

//   // Save file
//   XLSX.writeFile(wb, `Fiche-client-${client.name}.xlsx`);
// }