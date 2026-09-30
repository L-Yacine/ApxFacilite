import * as XLSX from "xlsx-js-style";
import { PrelevementSheet } from "@/types/installment";
import { format } from "date-fns";

export function exportPrelevementSheet(data: PrelevementSheet[], selectedDate: Date) {
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

  // Initialize workbook and worksheet
  const wb = XLSX.utils.book_new();
  const ws: any = {};

  // Set column widths
  ws['!cols'] = [
    { wch: 5 },  // Index
    { wch: 15 }, // Account Number
    { wch: 8 },  // Key
    { wch: 20 }, // Name
    { wch: 20 }, // Firstname
    { wch: 12 }, // Amount
    { wch: 15 }, // Store Account
    { wch: 8 },  // Store Key
    { wch: 15 }, // Start Date
    { wch: 15 }, // End Date
    { wch: 15 }, // Creation Date
    { wch: 8 },  // M Rate
    { wch: 12 },  // N echeances
    { wch: 8 },  // PRLV
    { wch: 15 },  // Reference
    { wch: 8 }  // New Column
  ];

  // Add title
  ws['A1'] = createStyledCell(`Fiche de Prélèvement : ${format(selectedDate, "MM/yyyy")}`, styles.title);

  // Add headers
  const headers = [
    "#", "CCP", "Clé", "Nom", "Prénom", "Montant VO",
    "Compte B", "Clé", "Date Debut", "Date Fin",
    "Date Creation", "M Rate","N Echeances", "PRLV", "Reference"
  ];

  headers.forEach((header, index) => {
    ws[XLSX.utils.encode_cell({ r: 2, c: index })] = createStyledCell(header, styles.tableHeader);
  });

  // Add data
  data.forEach((row, rowIndex) => {
    const dataRow = [
      rowIndex + 1,
      row.account_number,
      row.account_key,
      row.name,
      row.firstname,
      row.amount,
      row.store_account,
      row.store_key,
      format(new Date(row.start_date), "dd/MM/yyyy"),
      format(new Date(row.end_date), "dd/MM/yyyy"),
      format(new Date(row.creation_date), "dd/MM/yyyy"),
      row.m_rate,
      row.number_of_installments,
      row.prlv,
      row.reference
    ];

    dataRow.forEach((value, colIndex) => {
      const cellStyle = { ...styles.tableCell };
      
      // Center align numbers and dates, left align text
      if (typeof value === 'number' || (value as any) instanceof Date || 
          (typeof value === 'string' && value.match(/^\d{1,2}\/\d{1,2}\/\d{4}/))) {
        cellStyle.alignment.horizontal = 'center';
      } else {
        cellStyle.alignment.horizontal = 'left';
      }

      ws[XLSX.utils.encode_cell({ r: rowIndex + 3, c: colIndex })] = createStyledCell(value, cellStyle);
    });
  });

  // Set worksheet range
  ws['!ref'] = `A1:O${data.length + 3}`;

  // Merge title cells
  ws['!merges'] = [
    { s: { r: 0, c: 0 }, e: { r: 0, c: 14 } } // Title row
  ];

  // Add worksheet to workbook
  XLSX.utils.book_append_sheet(wb, ws, "Prelevement Sheet");

  // Save file with selected date
  const fileDate = format(selectedDate, "MM-yyyy");
  XLSX.writeFile(wb, `Fiche-de-prelevement-${fileDate}.xlsx`);
}
