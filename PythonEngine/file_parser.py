# PythonEngine/file_parser.py
import pandas as pd
from docx import Document
import os
from PyPDF2 import PdfReader
from pdf2image import convert_from_path
import pytesseract
from PIL import Image
import sys

# Set Tesseract path explicitly
pytesseract.pytesseract.tesseract_cmd = r'C:\Program Files\Tesseract-OCR\tesseract.exe'

def extract_text_from_pdf(file_path):
    try:
        reader = PdfReader(file_path)
        text = ""
        for page in reader.pages:
            text += page.extract_text() or ""
        return text.strip()
    except Exception as e:
        return f"[GAGAL] Gagal membaca PDF: {e}"

def extract_text_from_scanned_pdf(file_path):
    try:
        # Debugging: Print Tesseract path
        print(f"[DEBUG] Menggunakan Tesseract di: {pytesseract.pytesseract.tesseract_cmd}")
        
        images = convert_from_path(file_path, dpi=300)
        text = ""
        for img in images:
            text += pytesseract.image_to_string(img)
        return text.strip()
    except Exception as e:
        return f"[GAGAL] Gagal OCR PDF: {e}"

def extract_from_image(file_path):
    try:
        # Debugging: Print Tesseract path
        print(f"[DEBUG] Menggunakan Tesseract di: {pytesseract.pytesseract.tesseract_cmd}")
        
        img = Image.open(file_path)
        text = pytesseract.image_to_string(img)
        return text.strip()
    except Exception as e:
        return f"[GAGAL] Gagal membaca gambar: {e}"



def extract_smart_snippet(text, parts=3):
    lines = text.splitlines()
    lines = [line.strip() for line in lines if line.strip()]
    if not lines:
        return ""
    chunk_size = max(len(lines) // parts, 1)
    snippet = []
    for i in range(parts):
        idx = min(i * chunk_size, len(lines) - 1)
        snippet.append(lines[idx])
    return "\n".join(snippet)

def extract_content(file_path):
    ext = os.path.splitext(file_path)[1].lower()

    try:
        if ext in [".xlsx", ".xls"]:
            df = pd.read_excel(file_path)
            num_rows = len(df)
            third = num_rows // 3
            snippet_df = pd.concat([
                df.head(10),
                df.iloc[third:third+10],
                df.tail(10)
            ])
            return snippet_df.to_string(index=False)

        elif ext == ".csv":
            df = pd.read_csv(file_path)
            num_rows = len(df)
            third = num_rows // 3
            snippet_df = pd.concat([
                df.head(10),
                df.iloc[third:third+10],
                df.tail(10)
            ])
            return snippet_df.to_string(index=False)

        elif ext == ".docx":
            doc = Document(file_path)
            paragraphs = [p.text.strip() for p in doc.paragraphs if p.text.strip()]
            if not paragraphs:
                return "[GAGAL] Tidak ada teks yang dapat dibaca dalam dokumen Word."
            return "\n".join(paragraphs)

        elif ext == ".pdf":
            text = extract_text_from_pdf(file_path)
            if len(text.strip()) < 100:
                print("[INFO] Teks PDF terlalu pendek, coba OCR...")
                text = extract_text_from_scanned_pdf(file_path)
            return extract_smart_snippet(text)

        elif ext in [".jpg", ".jpeg", ".png"]:
            text = extract_text_from_image(file_path)
            return extract_smart_snippet(text)

        else:
            return "[GAGAL] Format file tidak didukung."

    except Exception as e:
        return f"[GAGAL] Gagal membaca file: {e}"
