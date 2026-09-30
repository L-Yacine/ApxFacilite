import * as XLSX from "xlsx-js-style";
import { format } from "date-fns";
import { StockLog } from "@/types/product";

// Type definitions for Excel styling
interface XlsxStyle {
  font?: {
    name?: string;
    sz?: number;
    bold?: boolean;
    color?: { rgb: string };
  };
  alignment?: {
    vertical?: "bottom" | "center" | "top";
    horizontal?: "left" | "center" | "right";
    wrapText?: boolean;
    readingOrder?: number;
    textRotation?: number;
  };
  fill?: {
    patternType: "solid" | "none";
    fgColor?: { rgb: string };
    bgColor?: { rgb: string };
  };
  border?: {
    top?: { style: BorderStyle; color: { rgb: string } };
    bottom?: { style: BorderStyle; color: { rgb: string } };
    left?: { style: BorderStyle; color: { rgb: string } };
    right?: { style: BorderStyle; color: { rgb: string } };
  };
}

type BorderStyle = "thin" | "medium" | "thick" | "dotted" | "hair" | "dashed";

// Predefined styles for consistent appearance
const STYLES = {
  title: {
    font: { name: "Calibri", sz: 16, bold: true },
    alignment: { vertical: "center", horizontal: "center" },
    fill: { patternType: "solid", fgColor: { rgb: "4472C4" } },
    border: {
      top: { style: "medium", color: { rgb: "000000" } },
      bottom: { style: "medium", color: { rgb: "000000" } },
      left: { style: "medium", color: { rgb: "000000" } },
      right: { style: "medium", color: { rgb: "000000" } },
    },
  },
  subtitle: {
    font: { name: "Calibri", sz: 12, bold: true },
    alignment: { vertical: "center", horizontal: "left" },
    fill: { patternType: "solid", fgColor: { rgb: "E9EFF7" } },
  },
  header: {
    font: { name: "Calibri", sz: 11, bold: true, color: { rgb: "FFFFFF" } },
    alignment: { vertical: "center", horizontal: "center" },
    fill: { patternType: "solid", fgColor: { rgb: "4472C4" } },
    border: {
      top: { style: "medium", color: { rgb: "000000" } },
      bottom: { style: "medium", color: { rgb: "000000" } },
      left: { style: "thin", color: { rgb: "000000" } },
      right: { style: "thin", color: { rgb: "000000" } },
    },
  },
  cell: {
    font: { name: "Calibri", sz: 11 },
    alignment: { vertical: "center" },
    border: {
      top: { style: "thin", color: { rgb: "000000" } },
      bottom: { style: "thin", color: { rgb: "000000" } },
      left: { style: "thin", color: { rgb: "000000" } },
      right: { style: "thin", color: { rgb: "000000" } },
    },
  },
} as const;

// Helper function to create styled cells
const createStyledCell = (value: any, style: XlsxStyle) => ({
  v: value,
  t: typeof value === "number" ? "n" : "s",
  s: style,
});

export function exportStockLogsSheet(
  stockLogs: StockLog[],
  productName: string
): void {
  // Initialize workbook and worksheet
  const wb = XLSX.utils.book_new();
  const ws: XLSX.WorkSheet = {};

  // Set column widths for better readability
  ws["!cols"] = [
    { wch: 22 }, // Date
    { wch: 12 }, // Type
    { wch: 12 }, // Quantity
    { wch: 12 }, // Before
    { wch: 12 }, // After
    { wch: 20 }, // Operator
    { wch: 40 }, // Observation
  ];

  // Add title and metadata
  ws["A1"] = createStyledCell("Fiche de stck", STYLES.title);
  ws["A2"] = createStyledCell(`Article: ${productName}`, STYLES.subtitle);
  ws["A3"] = createStyledCell(
    `Date: ${format(new Date(), "PPP")}`,
    STYLES.subtitle
  );

  // Add headers
  const headers = [
    "Date",
    "Type",
    "Quantité",
    "Q_Avant",
    "Q_Aprés",
    "Operateur",
    "Observation",
  ];
  headers.forEach((header, idx) => {
    ws[XLSX.utils.encode_cell({ r: 4, c: idx })] = createStyledCell(
      header,
      STYLES.header
    );
  });

  // Add data rows
  stockLogs.forEach((log, rowIdx) => {
    const row = [
      format(new Date(log.date), "PPp"),
      log.type === "incoming" ? "Entrée" : "Sortie",
      log.quantity,
      log.before_stock,
      log.after_stock,
      log.operator_name,
      log.observation || "-",
    ];

    row.forEach((value, colIdx) => {
      const cellStyle = { ...STYLES.cell };

      // Column-specific styling
      switch (colIdx) {
        case 0: // Date
          cellStyle.alignment = { ...cellStyle.alignment };
          break;
        case 1: // Type
          cellStyle.alignment = { ...cellStyle.alignment };
          if (log.type === "incoming") {
            cellStyle.font = { ...cellStyle.font };
          } else if (log.type === "outgoing") {
            cellStyle.font = { ...cellStyle.font };
          }
          break;
        case 2: // Quantity
        case 3: // Before
        case 4: // After
          cellStyle.alignment = { ...cellStyle.alignment };
          break;
        case 6: // Observation
          cellStyle.alignment = { ...cellStyle.alignment };
          break;
        default:
          cellStyle.alignment = { ...cellStyle.alignment };
      }

      ws[XLSX.utils.encode_cell({ r: rowIdx + 5, c: colIdx })] =
        createStyledCell(value, cellStyle);
    });
  });

  // Set worksheet range
  ws["!ref"] = `A1:G${stockLogs.length + 5}`;

  // Merge title and info cells
  ws["!merges"] = [
    { s: { r: 0, c: 0 }, e: { r: 0, c: 6 } }, // Title
    { s: { r: 1, c: 0 }, e: { r: 1, c: 6 } }, // Product info
    { s: { r: 2, c: 0 }, e: { r: 2, c: 6 } }, // Export date
  ];

  // Add worksheet to workbook and save
  XLSX.utils.book_append_sheet(wb, ws, "Stock Logs");
  const fileName = `stock-logs-${productName}-${format(
    new Date(),
    "yyyy-MM-dd"
  )}.xlsx`;
  XLSX.writeFile(wb, fileName);
}
