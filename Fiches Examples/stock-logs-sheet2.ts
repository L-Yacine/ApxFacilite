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
    { wch: 20 }, // Date
    { wch: 12 }, // Before Stock
    { wch: 12 }, // Incoming Quantity
    { wch: 15 }, // Incoming Operator Name
    { wch: 12 }, // Outgoing Quantity
    { wch: 15 }, // Outgoing Operator Name
    { wch: 12 }, // After Stock
  ];

  // Add title and metadata
  ws["A1"] = createStyledCell("Stock Logs", STYLES.title);
  ws["A2"] = createStyledCell(`Article: ${productName}`, STYLES.subtitle);
  ws["A3"] = createStyledCell(
    `Date: ${format(new Date(), "dd/mm/yyyy")}`,
    STYLES.subtitle
  );

  // Add headers
  const headers = [
    "Date",
    "Nombre",
    "Entré",
    "Nom client",
    "Sortie",
    "Nom fournisseur",
    "Reste",
  ];
  headers.forEach((header, idx) => {
    ws[XLSX.utils.encode_cell({ r: 4, c: idx })] = createStyledCell(
      header,
      STYLES.header
    );
  });

  // Add subheaders
  const subheaders = ["", "", "NB", "Nom", "NB", "Nom", ""];
  subheaders.forEach((subheader, idx) => {
    ws[XLSX.utils.encode_cell({ r: 5, c: idx })] = createStyledCell(
      subheader,
      STYLES.header
    );
  });

  // Add data rows
  stockLogs.forEach((log, rowIdx) => {
    const row = [
      format(new Date(log.date), "dd/mm/yyyy"),
      log.before_stock,
      log.type === "incoming" ? log.quantity : "-",
      log.type === "incoming" ? log.operator_name : "-",
      log.type === "outgoing" ? log.quantity : "-",
      log.type === "outgoing" ? log.operator_name : "-",
      log.after_stock,
    ];

    row.forEach((value, colIdx) => {
      const cellStyle = { ...STYLES.cell };

      ws[XLSX.utils.encode_cell({ r: rowIdx + 6, c: colIdx })] =
        createStyledCell(value, cellStyle);
    });
  });

  // Set worksheet range
  ws["!ref"] = `A1:G${stockLogs.length + 6}`;

  // Merge title and info cells
  ws["!merges"] = [
    { s: { r: 0, c: 0 }, e: { r: 0, c: 6 } }, // Title
    { s: { r: 1, c: 0 }, e: { r: 1, c: 6 } }, // Product info
    { s: { r: 2, c: 0 }, e: { r: 2, c: 6 } }, // Export date
    { s: { r: 4, c: 2 }, e: { r: 4, c: 3 } }, // Incoming header
    { s: { r: 4, c: 4 }, e: { r: 4, c: 5 } }, // Outgoing header
  ];

  // Add worksheet to workbook and save
  XLSX.utils.book_append_sheet(wb, ws, "Stock Logs");
  const fileName = `stock-logs-${productName}-${format(
    new Date(),
    "yyyy-MM-dd"
  )}.xlsx`;
  XLSX.writeFile(wb, fileName);
}