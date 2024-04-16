// Import necessary libraries
import { Component } from '@angular/core';
import jsPDF from 'jspdf';
import html2canvas from 'html2canvas';

@Component({
  selector: 'app-pdf-generator',
  templateUrl: './pdf-generator.component.html',
  styleUrls: ['./pdf-generator.component.css']
})
export class PdfGeneratorComponent {

  constructor() { }

  // Method to capture HTML and convert it into a PDF
  public downloadPdf(): void {
    const data = document.getElementById('contentToConvert') as HTMLElement;

    html2canvas(data, { scale: 1 }).then(canvas => {
      const contentDataURL = canvas.toDataURL('image/png');
      let pdf = new jsPDF('p', 'mm', 'a4'); // Creates PDF in portrait mode using A4 size
      let imgHeight = canvas.height * 208 / canvas.width;
      let heightLeft = imgHeight;
      let position = 0;

      pdf.addImage(contentDataURL, 'PNG', 0, position, 208, imgHeight);
      heightLeft -= pdf.internal.pageSize.height;

      while (heightLeft >= 0) {
        position = heightLeft - imgHeight;
        pdf.addPage();
        pdf.addImage(contentDataURL, 'PNG', 0, position, 208, imgHeight);
        heightLeft -= pdf.internal.pageSize.height;
      }

      pdf.save('download.pdf');
    });
  }
}
